using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// What a central Submission Agent can reach outside their own (empty) agency: the people they
/// need to message, and the sign-in path they take to get there.
/// </summary>
public class SubmissionAgentReachTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public SubmissionAgentReachTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "Reach!12345";

    private static async Task<string> CreateAsync(HttpClient superAdmin, Guid agencyId, string role)
    {
        var userName = $"rx{Guid.NewGuid():N}"[..16];
        await superAdmin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com", userName, password = Password,
            agencyId, roles = new[] { role },
        });
        return userName;
    }

    /// <summary>
    /// The chat member picker is fed from the user directory. A central agent belongs to no
    /// agency, so the ordinary scoping narrowed that to platform-level accounts — the "New
    /// conversation" list showed a handful of SuperAdmins and nobody they actually work with.
    /// They verify every company's sales; they have to be able to message the closers who made them.
    /// </summary>
    [Fact]
    public async Task A_central_agent_sees_every_agencys_people_in_the_directory()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var agency = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();

        var closerName = await CreateAsync(sa, agency, "Closer");
        var centralName = await CreateAsync(sa, Guid.Empty, "Validator");

        var central = await _factory.LoginAsync(centralName, Password);
        var directory = await central.GetJsonAsync("/api/users/directory");

        Assert.Contains(directory.EnumerateArray(),
            u => (u.GetProperty("userName").GetString() ?? "") == closerName);
    }

    /// <summary>
    /// An ordinary agency user's directory stays their own agency. Widening the picker for
    /// cross-agency staff must not open the whole platform's names to everyone.
    /// </summary>
    [Fact]
    public async Task An_agency_user_still_only_sees_their_own_agency()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var home = (await sa.GetJsonAsync("/api/agencies")).EnumerateArray().First().GetProperty("id").GetGuid();

        var res = await sa.PostAsJsonAsync("/api/agencies", new
        {
            name = $"Away {Guid.NewGuid():N}"[..18],
            code = $"C{Random.Shared.Next(100000, 999999)}",
            ceoName = "Away Chief", ceoEmail = $"ceo-{Guid.NewGuid():N}@example.com",
        });
        res.EnsureSuccessStatusCode();
        var away = (await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();

        var outsiderName = await CreateAsync(sa, away, "Closer");
        var insiderName = await CreateAsync(sa, home, "Closer");

        var insider = await _factory.LoginAsync(insiderName, Password);
        var directory = await insider.GetJsonAsync("/api/users/directory");
        var names = directory.EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();

        Assert.DoesNotContain(outsiderName, names);
    }

    /// <summary>
    /// Signing in as a central Submission Agent no longer demands two-factor enrolment first.
    ///
    /// Removed at the owner's instruction: these are floor staff on shared machines and the
    /// enrolment gate was blocking the work. Privileged roles are still forced — see the
    /// RequireTwoFactor list — and any agent may still enable it themselves.
    /// </summary>
    [Fact]
    public async Task A_central_agent_is_not_forced_into_two_factor()
    {
        var sa = await _factory.LoginSuperAdminAsync();
        var centralName = await CreateAsync(sa, Guid.Empty, "Validator");

        var anon = _factory.CreateClient();
        var login = await anon.PostJsonAsync("/api/auth/login",
            new { userNameOrEmail = centralName, password = Password });

        // The claim that drives the "enrol in 2FA before anything else" gate must be absent.
        var hasGate = login.TryGetProperty("twoFactorSetupRequired", out var flag)
                      && flag.ValueKind == System.Text.Json.JsonValueKind.True;
        Assert.False(hasGate, "a submission agent should not be held at a two-factor enrolment screen");
    }
}
