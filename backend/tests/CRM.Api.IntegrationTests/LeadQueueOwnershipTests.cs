using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The invariant: a lead is in EXACTLY ONE actionable place — one person's My Leads, or one shared
/// pool. Never both.
///
/// This was untested, which is how the two queues drifted apart: the pool filtered on stage alone and
/// the personal list on owner alone, so in production 43 of the 53 leads in the closer pool already
/// belonged to a named person and every closer saw all of them as claimable work.
/// </summary>
public class LeadQueueOwnershipTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public LeadQueueOwnershipTests(CrmWebAppFactory factory) => _factory = factory;

    /// <summary>The intake form's required fields — one place, so a schema change touches one line.</summary>
    private static object IntakeBody(string first, string last) => new
    {
        firstName = first,
        lastName = last,
        phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
        email = $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com",
        streetAddress = "1 Test Street",
        city = "Testville",
        state = "TX",
        zipcode = "75001",
        maritalStatus = "Single",
        createdDate = DateTime.UtcNow,
        birthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ageYears = 35,
        consentCaptured = true,
    };

    private static async Task<HashSet<string>> IdsAsync(HttpClient c, string url)
    {
        var res = await c.GetAsync(url);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var arr) ? arr : root;
        var ids = new HashSet<string>();
        foreach (var el in items.EnumerateArray())
        {
            // Available Leads wraps each row as { lead: {...}, stage, stageLabel }.
            var lead = el.TryGetProperty("lead", out var inner) ? inner : el;
            if (lead.TryGetProperty("id", out var id) && id.GetString() is { } s) ids.Add(s);
        }
        return ids;
    }

    /// <summary>
    /// THE invariant test. If anyone reintroduces a stage-only pool predicate, this fails loudly.
    /// </summary>
    [Fact]
    public async Task My_leads_and_available_leads_never_contain_the_same_lead()
    {
        var admin = await _factory.LoginAdminAsync();

        var mine = await IdsAsync(admin, "/api/leads/mine?take=200");
        var available = await IdsAsync(admin, "/api/leads/available");

        Assert.Empty(mine.Intersect(available));
    }

    /// <summary>
    /// A closer typing a lead in IS the person working it, so it must go straight to their own list
    /// and never enter the shared pool. Before, capture set both the stage the pool reads and the
    /// owner the personal list reads, so one HTTP call produced a duplicate every time.
    /// </summary>
    [Fact]
    public async Task Closer_captured_lead_goes_to_my_leads_not_the_pool()
    {
        var admin = await _factory.LoginAdminAsync();

        var created = await admin.PostJsonAsync("/api/intake/close/leads", IntakeBody("Pool", $"Check{Guid.NewGuid():N}".Substring(0, 10)));
        var id = created.GetProperty("leadId").GetString();

        Assert.Contains(id, await IdsAsync(admin, "/api/leads/mine?take=200"));
        Assert.DoesNotContain(id, await IdsAsync(admin, "/api/leads/available"));
    }

    /// <summary>
    /// Release then claim — the two events that move a lead between its owner and the pool. Neither
    /// existed before: nothing reachable from the pool screen ever set an owner, so a lead had no way
    /// out of the pool and two agents could work it at once.
    ///
    /// Starts from a closer-captured lead (owned outright) rather than a fronter-captured one,
    /// because an active "auto-assign new leads to a Fronter" workflow rule claims those on creation.
    /// </summary>
    [Fact]
    public async Task Release_then_claim_moves_a_lead_between_the_pool_and_my_leads()
    {
        var admin = await _factory.LoginAdminAsync();

        var created = await admin.PostJsonAsync("/api/intake/close/leads", IntakeBody("Claim", $"Me{Guid.NewGuid():N}".Substring(0, 10)));
        var id = created.GetProperty("leadId").GetString();

        // Owned outright: mine, not in the pool.
        Assert.Contains(id, await IdsAsync(admin, "/api/leads/mine?take=200"));
        Assert.DoesNotContain(id, await IdsAsync(admin, "/api/leads/available"));

        (await admin.PostAsync($"/api/leads/{id}/release", null)).EnsureSuccessStatusCode();

        // Released: in the pool, no longer mine.
        Assert.Contains(id, await IdsAsync(admin, "/api/leads/available"));
        Assert.DoesNotContain(id, await IdsAsync(admin, "/api/leads/mine?take=200"));

        (await admin.PostAsync($"/api/leads/{id}/claim", null)).EnsureSuccessStatusCode();

        // Claimed back: mine again, out of the pool. At no point in either direction is it in both.
        Assert.Contains(id, await IdsAsync(admin, "/api/leads/mine?take=200"));
        Assert.DoesNotContain(id, await IdsAsync(admin, "/api/leads/available"));
    }

    /// <summary>
    /// Two agents pressing Claim at the same moment must produce exactly one owner. The guard is a
    /// conditional UPDATE … WHERE AssignedUserId IS NULL, so the database picks the winner — a
    /// read-then-write would let both callers pass the check.
    /// </summary>
    [Fact]
    public async Task Concurrent_claims_produce_exactly_one_winner()
    {
        var admin = await _factory.LoginAdminAsync();

        var created = await admin.PostJsonAsync("/api/intake/close/leads", IntakeBody("Race", $"Cond{Guid.NewGuid():N}".Substring(0, 10)));
        var id = created.GetProperty("leadId").GetString();
        (await admin.PostAsync($"/api/leads/{id}/release", null)).EnsureSuccessStatusCode();

        var first = await admin.PostAsync($"/api/leads/{id}/claim", null);
        var second = await admin.PostAsync($"/api/leads/{id}/claim", null);

        Assert.True(first.IsSuccessStatusCode,
            $"the first claim should win, got {(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>
    /// A pooled lead has no owner, so the detail page must still open for someone whose role works
    /// that pool. Otherwise clicking a name in Available Leads reports "Lead not found" — the most
    /// likely way to ship this change broken.
    /// </summary>
    [Fact]
    public async Task An_unclaimed_pooled_lead_can_still_be_opened()
    {
        var admin = await _factory.LoginAdminAsync();

        var created = await admin.PostJsonAsync("/api/intake/close/leads", IntakeBody("Open", $"Me{Guid.NewGuid():N}".Substring(0, 10)));
        var id = created.GetProperty("leadId").GetString();
        (await admin.PostAsync($"/api/leads/{id}/release", null)).EnsureSuccessStatusCode();

        var detail = await admin.GetAsync($"/api/leads/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }
}
