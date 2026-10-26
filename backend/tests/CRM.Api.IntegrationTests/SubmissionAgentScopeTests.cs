using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The Submission Agents screen has to show agents that are CONFINED to one agency.
///
/// It listed only central agents — those belonging to no agency — which hid the exact failure it
/// exists to prevent. An agent created inside an agency holds the same Validator role, looks like
/// a submission agent to everyone, and can only ever see that one company's sales. Nothing on the
/// screen said so, so "the submission agent cannot see other call centres" had no visible cause
/// and no obvious cure.
/// </summary>
public class SubmissionAgentScopeTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public SubmissionAgentScopeTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Scope!12345";

    private static async Task<(Guid Id, string UserName)> CreateValidatorAsync(HttpClient superAdmin, Guid agencyId)
    {
        var userName = $"sa{Guid.NewGuid():N}"[..16];
        var created = await superAdmin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId, roles = new[] { "Validator" },
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    /// <summary>
    /// An agency-scoped submission agent appears, named by the single company they can see; a
    /// central one appears with no agency at all. The difference is the whole point of the column.
    /// </summary>
    [Fact]
    public async Task The_roster_shows_which_agents_are_confined_to_one_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var agency = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();

        var (confinedId, _) = await CreateValidatorAsync(sa, agency);
        var (centralId, _) = await CreateValidatorAsync(sa, Guid.Empty);

        var roster = await sa.GetJsonAsync("/api/agencies/submission-agents");
        var rows = roster.EnumerateArray().ToList();

        var confined = rows.Single(r => r.GetProperty("id").GetGuid() == confinedId);
        Assert.False(string.IsNullOrWhiteSpace(confined.GetProperty("agencyName").GetString()),
            "an agent tied to one company must say which, or the screen hides the problem it exists to show");

        var central = rows.Single(r => r.GetProperty("id").GetGuid() == centralId);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, central.GetProperty("agencyName").ValueKind);
    }

    /// <summary>
    /// And the cure works from there: widening the agent to the whole platform makes them central,
    /// which is what actually lets them see other agencies' call centres.
    /// </summary>
    [Fact]
    public async Task Widening_a_confined_agent_makes_them_central()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var agency = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();
        var (id, _) = await CreateValidatorAsync(sa, agency);

        await sa.PutJsonAsync($"/api/admin/users/{id}/agency", new { agencyId = (Guid?)null });

        var roster = await sa.GetJsonAsync("/api/agencies/submission-agents");
        var row = roster.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("agencyName").ValueKind);
    }
}
