using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Removing a closed or sold lead.
///
/// This withdraws a sale and the commission paid on it, so the tests are mostly about the things
/// that must NOT happen: no hard delete, no removal by someone who shouldn't, no removal of live
/// pipeline work, and no sale left behind still counting toward revenue once its lead is gone.
/// </summary>
public class DeleteLeadTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public DeleteLeadTests(CrmWebAppFactory factory) => _factory = factory;

    private static object Lead(string first) => new
    {
        firstName = first,
        lastName = "Removable",
        maritalStatus = "Single",
        createdDate = DateTime.UtcNow,
        streetAddress = "27 S 750 E",
        city = "Phoenix",
        state = "AZ",
        zipcode = "85004",
        phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
        birthDate = new DateTime(1962, 4, 11),
        ageYears = 64,
        email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
        jornayaLeadId = Guid.NewGuid().ToString("N"),
    };

    private static object Application() => new
    {
        healthConditions = "None disclosed",
        gender = "Female", age = 64, smokerStatus = "Non-smoker",
        name = "Sold Removable", dateOfBirth = new DateTime(1962, 4, 11),
        address = "27 S 750 E, Phoenix AZ 85004",
        carrier = "Mutual of Omaha", plan = "Whole Life",
        faceAmount = 10000m, premium = 40m,
        email = $"sold-{Guid.NewGuid():N}@example.com",
        beneficiary = "Ana Removable", secondBeneficiary = (string?)null,
        initialDraftDate = DateTime.UtcNow.AddDays(14), futureDraftDate = (DateTime?)null,
        phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}", altPhone = (string?)null,
        primaryDoctor = "Dr Hayden", social = "000-00-0000", bornIn = "Arizona",
        driversLicense = "D1234567", height = "5'4\"", weight = "150",
        accountType = "Checking", bankName = "Wells Fargo",
        accountNumber = "000123456789", routingNumber = "121000248",
        banking198Reason = "Customer confirmed the account on the recorded line.",
    };

    /// <summary>Creates a lead, closes it as sold, and returns the lead id.</summary>
    private async Task<Guid> SoldLeadAsync(HttpClient admin)
    {
        var userName = $"cl{Guid.NewGuid():N}"[..16];
        const string password = "CloserDel!123";
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password, roles = new[] { "Closer" },
        });
        var closer = await _factory.LoginAsync(userName, password);

        var created = await closer.PostJsonAsync("/api/intake/close/leads", Lead("Sold"));
        var leadId = created.GetProperty("leadId").GetGuid();

        await closer.PostJsonAsync($"/api/intake/close/{leadId}",
            new { status = "CompleteAndSold", application = Application() });
        return leadId;
    }

    [Fact]
    public async Task An_admin_can_remove_a_sold_lead_and_it_leaves_every_list()
    {
        var admin = await _factory.LoginAdminAsync();
        var leadId = await SoldLeadAsync(admin);

        var res = await admin.DeleteAsJsonAsync($"/api/leads/{leadId}",
            new { reason = "Duplicate of an earlier policy for the same customer." });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        // Gone from the pipeline…
        var list = await admin.GetJsonAsync("/api/leads?take=200");
        var rows = list.TryGetProperty("items", out var items) ? items : list;
        Assert.DoesNotContain(rows.EnumerateArray(), l => l.GetProperty("id").GetGuid() == leadId);

        // …and from its own detail page.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/leads/{leadId}")).StatusCode);
    }

    /// <summary>
    /// The money has to go with it. A sale left behind would keep a deleted lead's revenue in every
    /// dashboard and keep paying commission on a policy that has been retired.
    /// </summary>
    [Fact]
    public async Task Removing_a_sold_lead_takes_its_sale_out_of_the_figures()
    {
        var admin = await _factory.LoginAdminAsync();
        var leadId = await SoldLeadAsync(admin);

        var before = await admin.GetJsonAsync("/api/sales?take=200");
        var beforeRows = before.TryGetProperty("items", out var bi) ? bi : before;
        Assert.Contains(beforeRows.EnumerateArray(), s => s.GetProperty("leadId").GetGuid() == leadId);

        await admin.DeleteAsJsonAsync($"/api/leads/{leadId}", new { reason = "Policy was never issued by the carrier." });

        var after = await admin.GetJsonAsync("/api/sales?take=200");
        var afterRows = after.TryGetProperty("items", out var ai) ? ai : after;
        Assert.DoesNotContain(afterRows.EnumerateArray(), s => s.GetProperty("leadId").GetGuid() == leadId);
    }

    /// <summary>Soft, not hard: the reason and the name must survive, or nobody can answer "where did it go?".</summary>
    [Fact]
    public async Task The_reason_and_who_removed_it_are_kept()
    {
        var admin = await _factory.LoginAdminAsync();
        var leadId = await SoldLeadAsync(admin);
        const string reason = "Customer cancelled during the free-look period.";

        await admin.DeleteAsJsonAsync($"/api/leads/{leadId}", new { reason });

        var audit = await admin.GetJsonAsync("/api/admin/audit?action=LeadDeleted");
        var rows = audit.TryGetProperty("items", out var items) ? items : audit;

        var mine = rows.EnumerateArray()
            .Where(a => a.GetProperty("entityId").GetString() == leadId.ToString()).ToList();

        Assert.NotEmpty(mine);
        Assert.Contains(reason, mine[0].GetProperty("changes").GetString() ?? "");
        Assert.False(string.IsNullOrWhiteSpace(mine[0].GetProperty("userName").GetString()),
            "the audit row must name the administrator who removed it");
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var admin = await _factory.LoginAdminAsync();
        var leadId = await SoldLeadAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.DeleteAsJsonAsync($"/api/leads/{leadId}", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.DeleteAsJsonAsync($"/api/leads/{leadId}", new { reason = "dup" })).StatusCode);

        // Still there, untouched.
        Assert.True((await admin.GetAsync($"/api/leads/{leadId}")).IsSuccessStatusCode);
    }

    /// <summary>
    /// This is for retiring finished work, not for clearing leads nobody wants to call. A lead still
    /// in play must not be removable.
    /// </summary>
    [Fact]
    public async Task A_lead_still_in_the_pipeline_cannot_be_removed()
    {
        var admin = await _factory.LoginAdminAsync();

        var userName = $"fr{Guid.NewGuid():N}"[..16];
        const string password = "FronterDel!123";
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password, roles = new[] { "Fronter" },
        });
        var fronter = await _factory.LoginAsync(userName, password);

        var created = await fronter.PostJsonAsync("/api/intake/leads", Lead("Live"));
        var leadId = created.GetProperty("leadId").GetGuid();

        var res = await admin.DeleteAsJsonAsync($"/api/leads/{leadId}", new { reason = "Trying to clear the queue." });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.True((await admin.GetAsync($"/api/leads/{leadId}")).IsSuccessStatusCode);
    }

    /// <summary>Withdrawing a sale is a different authority from working one.</summary>
    [Fact]
    public async Task A_closer_cannot_remove_a_lead()
    {
        var admin = await _factory.LoginAdminAsync();
        var leadId = await SoldLeadAsync(admin);

        var userName = $"nd{Guid.NewGuid():N}"[..16];
        const string password = "NoDelete!123";
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password, roles = new[] { "Closer" },
        });
        var closer = await _factory.LoginAsync(userName, password);

        var res = await closer.DeleteAsJsonAsync($"/api/leads/{leadId}", new { reason = "I would rather this went away." });
        Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"a closer must not be able to remove a lead; got {res.StatusCode}");

        Assert.True((await admin.GetAsync($"/api/leads/{leadId}")).IsSuccessStatusCode);
    }
}
