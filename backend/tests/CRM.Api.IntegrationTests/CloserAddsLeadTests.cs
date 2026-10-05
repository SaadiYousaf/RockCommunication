using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// A Closer adding a lead of their own — a referral, a call-back, an inbound they already worked.
///
/// There are two capture endpoints and each is restricted to its own role, which is easy to get
/// wrong from the client: the sidebar offered "Add Lead" to Closers while the form posted to the
/// FRONTER endpoint, so every Closer who tried filled the whole form in and was refused on submit.
/// These tests pin down which role may use which, and what the lead looks like afterwards.
/// </summary>
public class CloserAddsLeadTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public CloserAddsLeadTests(CrmWebAppFactory factory) => _factory = factory;

    private static object Lead(string first) => new
    {
        firstName = first,
        lastName = "Testcase",
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

    /// <summary>Signs in a brand-new user holding exactly one role.</summary>
    private async Task<HttpClient> AsRoleAsync(string role)
    {
        var admin = await _factory.LoginAdminAsync();
        var userName = $"{role.ToLowerInvariant()}{Guid.NewGuid():N}"[..16];
        const string password = "RoleUnderTest!1";

        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com",
            userName,
            password,
            roles = new[] { role },
        });
        return await _factory.LoginAsync(userName, password);
    }

    /// <summary>
    /// The whole point. A Closer's own lead lands in their work as Verified and assigned to them —
    /// they have already had the conversation a Fronter and Verifier would otherwise have.
    /// </summary>
    [Fact]
    public async Task A_closer_can_add_a_lead_straight_into_their_own_pipeline()
    {
        var closer = await AsRoleAsync("Closer");

        var created = await closer.PostJsonAsync("/api/intake/close/leads", Lead("Carmen"));
        var id = created.GetProperty("leadId").GetGuid();

        // Captured past the fronter and verifier steps, which is the whole point of this path.
        Assert.Equal("Verified", created.GetProperty("stage").GetString());

        // And it is theirs to work: the closing screen for it opens, which it would not if the lead
        // belonged to somebody else. (It is NOT in the closer QUEUE — that holds unclaimed leads,
        // and this one already has an owner.)
        var closing = await closer.GetAsync($"/api/intake/close/{id}");
        Assert.True(closing.IsSuccessStatusCode,
            $"a closer should be able to open the lead they just added; got {closing.StatusCode}");
    }

    /// <summary>
    /// The endpoint the form used to post to for everyone. A Closer is not allowed on it — which is
    /// exactly why sending them there made "Add Lead" look broken rather than forbidden.
    /// </summary>
    [Fact]
    public async Task A_closer_is_refused_by_the_fronter_capture_endpoint()
    {
        var closer = await AsRoleAsync("Closer");

        var res = await closer.PostAsJsonAsync("/api/intake/leads", Lead("Refused"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_fronter_can_still_capture_through_the_fronter_endpoint()
    {
        var fronter = await AsRoleAsync("Fronter");

        var created = await fronter.PostJsonAsync("/api/intake/leads", Lead("Frontedlead"));
        Assert.NotEqual(Guid.Empty, created.GetProperty("leadId").GetGuid());
    }

    /// <summary>
    /// The whole job, end to end: a Closer adds their own lead, takes the application, and the sale
    /// is recorded.
    ///
    /// Worth doing as one test rather than three, because what broke in production was not any
    /// single step — each worked — but the fact that nothing in the UI joined them up. The closing
    /// screen was reachable only from a queue page the sidebar no longer pointed at, so a Closer
    /// could add a lead and then had nowhere to take it.
    /// </summary>
    [Fact]
    public async Task A_closer_can_take_their_own_lead_all_the_way_to_a_recorded_sale()
    {
        var closer = await AsRoleAsync("Closer");

        var created = await closer.PostJsonAsync("/api/intake/close/leads", Lead("Marisol"));
        var leadId = created.GetProperty("leadId").GetGuid();

        var result = await closer.PostJsonAsync($"/api/intake/close/{leadId}", new
        {
            status = "CompleteAndSold",
            application = Application(),
        });

        Assert.Equal("CompleteAndSold", result.GetProperty("status").GetString());

        // A sale actually exists on the other side of it — the point of the whole exercise.
        Assert.True(result.TryGetProperty("saleId", out var saleId) && saleId.ValueKind != JsonValueKind.Null,
            "closing a lead as sold should record a sale");
    }

    /// <summary>A complete application. Every field is required; the closer types them on the call.</summary>
    private static object Application() => new
    {
        healthConditions = "None disclosed",
        gender = "Female",
        age = 64,
        smokerStatus = "Non-smoker",
        name = "Marisol Testcase",
        dateOfBirth = new DateTime(1962, 4, 11),
        address = "27 S 750 E, Phoenix AZ 85004",
        carrier = "Mutual of Omaha",
        plan = "Whole Life",
        faceAmount = 10000m,
        premium = 40m,
        email = $"marisol-{Guid.NewGuid():N}@example.com",
        beneficiary = "Ana Testcase (daughter)",
        secondBeneficiary = (string?)null,
        initialDraftDate = DateTime.UtcNow.AddDays(14),
        futureDraftDate = (DateTime?)null,
        phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
        altPhone = (string?)null,
        primaryDoctor = "Dr Hayden",
        social = "000-00-0000",
        bornIn = "Arizona",
        driversLicense = "D1234567",
        height = "5'4\"",
        weight = "150",
        accountType = "Checking",
        bankName = "Wells Fargo",
        accountNumber = "000123456789",
        routingNumber = "121000248",
        // The bank check flags this account (code 198), which a closer resolves on the call by
        // recording why it is safe to proceed. Supplying it is part of the real flow, not a
        // shortcut around it.
        banking198Reason = "Customer confirmed the account on the recorded line.",
    };

    /// <summary>
    /// "Referred to HO" — the submission agent cannot resolve it from the floor and Head Office is
    /// now carrying it. It is open work, not an outcome: the sale stays at Closed rather than moving
    /// to Validated, Funded or Lost, and no commission is settled on it either way.
    /// </summary>
    [Fact]
    public async Task A_sale_can_be_referred_to_head_office()
    {
        var admin = await _factory.LoginAdminAsync();
        var closer = await AsRoleAsync("Closer");

        var created = await closer.PostJsonAsync("/api/intake/close/leads", Lead("Referred"));
        var leadId = created.GetProperty("leadId").GetGuid();
        var sold = await closer.PostJsonAsync($"/api/intake/close/{leadId}",
            new { status = "CompleteAndSold", application = Application() });
        var saleId = sold.GetProperty("saleId").GetGuid();

        var result = await admin.PostJsonAsync($"/api/intake/validate/{saleId}/status",
            new { status = "ReferredToHo" });

        Assert.Equal("ReferredToHo", result.GetProperty("status").GetString());
        Assert.Equal("Closed", result.GetProperty("leadStage").GetString());
    }

    /// <summary>Each capture path stays closed to the other role's holder.</summary>
    [Fact]
    public async Task A_fronter_is_refused_by_the_closer_capture_endpoint()
    {
        var fronter = await AsRoleAsync("Fronter");

        var res = await fronter.PostAsJsonAsync("/api/intake/close/leads", Lead("Wrongdoor"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
