using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Administrative correction of a lead's details and of the policy on a sale.
///
/// Before this, a lead's only editable field was its notes and a sale could not be corrected at all,
/// so a mistyped phone number or a premium keyed as 400 instead of 40 was permanent. The tests cover
/// the two things that make a correction safe rather than destructive: an omitted field is left
/// alone, and the change is recorded with its before and after.
/// </summary>
public class AdminEditTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public AdminEditTests(CrmWebAppFactory factory) => _factory = factory;

    private static object Lead(string first) => new
    {
        firstName = first, lastName = "Editable", maritalStatus = "Single",
        createdDate = DateTime.UtcNow, streetAddress = "27 S 750 E", city = "Phoenix",
        state = "AZ", zipcode = "85004",
        phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
        birthDate = new DateTime(1962, 4, 11), ageYears = 64,
        email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
        jornayaLeadId = Guid.NewGuid().ToString("N"),
    };

    private async Task<(HttpClient Admin, Guid LeadId)> AFrontedLeadAsync()
    {
        var admin = await _factory.LoginAdminAsync();
        var userName = $"fr{Guid.NewGuid():N}"[..16];
        const string password = "EditTest!123";
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password, roles = new[] { "Fronter" },
        });
        var fronter = await _factory.LoginAsync(userName, password);
        var created = await fronter.PostJsonAsync("/api/intake/leads", Lead("Edna"));
        return (admin, created.GetProperty("leadId").GetGuid());
    }

    [Fact]
    public async Task An_admin_can_correct_a_leads_details()
    {
        var (admin, leadId) = await AFrontedLeadAsync();

        var res = await admin.PutAsJsonAsync($"/api/leads/{leadId}/details", new
        {
            phoneNumber = "5559998888",
            email = "corrected@example.com",
            city = "Tucson",
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var lead = await admin.GetJsonAsync($"/api/leads/{leadId}");
        Assert.Equal("5559998888", lead.GetProperty("phoneNumber").GetString());
        Assert.Equal("corrected@example.com", lead.GetProperty("email").GetString());
        Assert.Equal("Tucson", lead.GetProperty("city").GetString());
    }

    /// <summary>
    /// THE property that makes a partial edit safe. Sending one field must not blank the rest —
    /// otherwise correcting a phone number quietly erases the address.
    /// </summary>
    [Fact]
    public async Task Fields_that_are_not_sent_are_left_alone()
    {
        var (admin, leadId) = await AFrontedLeadAsync();
        var before = await admin.GetJsonAsync($"/api/leads/{leadId}");
        var originalLast = before.GetProperty("lastName").GetString();
        var originalState = before.GetProperty("state").GetString();

        await admin.PutAsJsonAsync($"/api/leads/{leadId}/details", new { firstName = "Edwina" });

        var after = await admin.GetJsonAsync($"/api/leads/{leadId}");
        Assert.Equal("Edwina", after.GetProperty("firstName").GetString());
        Assert.Equal(originalLast, after.GetProperty("lastName").GetString());
        Assert.Equal(originalState, after.GetProperty("state").GetString());
    }

    /// <summary>A correction to a customer record has to be answerable later: who, and from what.</summary>
    [Fact]
    public async Task The_change_is_recorded_with_its_before_and_after()
    {
        var (admin, leadId) = await AFrontedLeadAsync();
        var before = await admin.GetJsonAsync($"/api/leads/{leadId}");
        var originalPhone = before.GetProperty("phoneNumber").GetString()!;

        await admin.PutAsJsonAsync($"/api/leads/{leadId}/details", new { phoneNumber = "5551112222" });

        var audit = await admin.GetJsonAsync("/api/admin/audit?action=LeadEditedByAdmin");
        var rows = audit.TryGetProperty("items", out var items) ? items : audit;
        var mine = rows.EnumerateArray()
            .Where(a => a.GetProperty("entityId").GetString() == leadId.ToString()).ToList();

        Assert.NotEmpty(mine);
        var changes = mine[0].GetProperty("changes").GetString() ?? "";
        Assert.Contains(originalPhone, changes);
        Assert.Contains("5551112222", changes);
    }

    [Fact]
    public async Task A_malformed_email_is_refused_and_nothing_changes()
    {
        var (admin, leadId) = await AFrontedLeadAsync();
        var before = await admin.GetJsonAsync($"/api/leads/{leadId}");

        var res = await admin.PutAsJsonAsync($"/api/leads/{leadId}/details", new { email = "not-an-address" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var after = await admin.GetJsonAsync($"/api/leads/{leadId}");
        Assert.Equal(before.GetProperty("email").GetString(), after.GetProperty("email").GetString());
    }

    /// <summary>Correcting a customer's record is an administrator's job, not an agent's.</summary>
    [Fact]
    public async Task An_agent_cannot_edit_a_lead()
    {
        var (admin, leadId) = await AFrontedLeadAsync();

        var userName = $"ag{Guid.NewGuid():N}"[..16];
        const string password = "NoEdit!1234";
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password, roles = new[] { "Closer" },
        });
        var agent = await _factory.LoginAsync(userName, password);

        var res = await agent.PutAsJsonAsync($"/api/leads/{leadId}/details", new { firstName = "Hijack" });
        Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"an agent must not be able to edit lead details; got {res.StatusCode}");
    }

    [Fact]
    public async Task An_unknown_lead_is_not_found()
    {
        var admin = await _factory.LoginAdminAsync();
        var res = await admin.PutAsJsonAsync($"/api/leads/{Guid.NewGuid()}/details", new { city = "Nowhere" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
