using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Changing someone's roles has to change what they can actually do.
///
/// Roles are carried in the access token's claims, so writing new rows to the database does nothing
/// to a token already sitting in somebody's browser. That made a role change advisory — the notice
/// asked the user to sign out and back in, and until they did, the API went on authorising them as
/// whoever they used to be.
///
/// It showed up on a real account: an agent was a TeamLead, was reassigned to Closer, and went on
/// being refused by the closing endpoints. The more serious direction is the other one — someone
/// stripped of a role keeps its access until they happen to sign out.
/// </summary>
public class RoleChangeTakesEffectTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public RoleChangeTakesEffectTests(CrmWebAppFactory factory) => _factory = factory;

    private const string Password = "RoleSwap!123";

    private async Task<(Guid Id, string UserName)> CreateAsync(HttpClient admin, string role)
    {
        var userName = $"swap{Guid.NewGuid():N}"[..16];
        var created = await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{userName}@example.com",
            userName,
            password = Password,
            roles = new[] { role },
        });
        return (created.GetProperty("id").GetGuid(), userName);
    }

    /// <summary>
    /// The reported case: TeamLead reassigned to Closer. After signing in again the closing
    /// endpoints accept them — the role they were given is the role they have.
    /// </summary>
    [Fact]
    public async Task A_reassigned_user_gets_their_new_access_on_the_next_sign_in()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, userName) = await CreateAsync(admin, "TeamLead");

        // As a TeamLead, the closer queue is not theirs.
        var asTeamLead = await _factory.LoginAsync(userName, Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await asTeamLead.GetAsync("/api/intake/close/queue")).StatusCode);

        await admin.PutJsonAsync($"/api/admin/users/{id}/roles", new { roles = new[] { "Closer" } });

        var asCloser = await _factory.LoginAsync(userName, Password);
        var res = await asCloser.GetAsync("/api/intake/close/queue");
        Assert.True(res.IsSuccessStatusCode,
            $"a user reassigned to Closer should reach the closer queue; got {res.StatusCode}");
    }

    /// <summary>
    /// The direction that matters for security: access removed is access gone. The old session is
    /// ended rather than left running on stale claims until it happens to expire.
    /// </summary>
    [Fact]
    public async Task Removing_a_role_ends_the_session_it_was_granted_to()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, userName) = await CreateAsync(admin, "Closer");

        var session = await _factory.LoginAsync(userName, Password);
        Assert.True((await session.GetAsync("/api/intake/close/queue")).IsSuccessStatusCode);

        // Capture the live refresh token before the role is taken away.
        var login = await session.PostJsonAsync("/api/auth/login",
            new { userNameOrEmail = userName, password = Password });
        var refreshToken = login.GetProperty("refreshToken").GetString();

        await admin.PutJsonAsync($"/api/admin/users/{id}/roles", new { roles = new[] { "Fronter" } });

        // That refresh token is dead, so the old session cannot mint a new one carrying the role
        // that was just removed.
        var anon = _factory.CreateClient();
        var refreshed = await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.False(refreshed.IsSuccessStatusCode,
            "revoking a role must end the sessions that still carry it");
    }

    /// <summary>A no-op save must not sign anybody out for nothing.</summary>
    [Fact]
    public async Task Reassigning_the_same_roles_leaves_the_session_alone()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, userName) = await CreateAsync(admin, "Closer");

        var login = await (await _factory.LoginAsync(userName, Password))
            .PostJsonAsync("/api/auth/login", new { userNameOrEmail = userName, password = Password });
        var refreshToken = login.GetProperty("refreshToken").GetString();

        await admin.PutJsonAsync($"/api/admin/users/{id}/roles", new { roles = new[] { "Closer" } });

        var anon = _factory.CreateClient();
        var refreshed = await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.True(refreshed.IsSuccessStatusCode,
            "saving the roles a user already has should not end their session");
    }
}
