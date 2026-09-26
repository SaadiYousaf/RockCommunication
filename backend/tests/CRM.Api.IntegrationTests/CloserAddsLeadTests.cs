using System.Net;
using System.Net.Http.Json;
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

    /// <summary>Each capture path stays closed to the other role's holder.</summary>
    [Fact]
    public async Task A_fronter_is_refused_by_the_closer_capture_endpoint()
    {
        var fronter = await AsRoleAsync("Fronter");

        var res = await fronter.PostAsJsonAsync("/api/intake/close/leads", Lead("Wrongdoor"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
