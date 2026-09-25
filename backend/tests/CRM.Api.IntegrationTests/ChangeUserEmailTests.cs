using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Changing the address a user signs in with.
///
/// The email is the login identity and the password-reset destination, so whoever controls it
/// controls the account. These tests are mostly about who is NOT allowed to do this, and about the
/// state left behind afterwards — an address that is changed without invalidating sessions or
/// clearing the confirmed flag is a half-done job that reads as finished.
/// </summary>
public class ChangeUserEmailTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public ChangeUserEmailTests(CrmWebAppFactory factory) => _factory = factory;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>Creates a plain agent the admin is allowed to manage, and returns its id.</summary>
    private static async Task<(Guid Id, string Email)> CreateAgentAsync(HttpClient admin)
    {
        var email = Unique("agent");
        var created = await admin.PostJsonAsync("/api/auth/register", new
        {
            email,
            userName = $"agent{Guid.NewGuid():N}"[..16],
            roles = new[] { "Fronter" },
        });
        return (created.GetProperty("id").GetGuid(), email);
    }

    [Fact]
    public async Task An_admin_can_change_a_users_email()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, _) = await CreateAgentAsync(admin);

        var next = Unique("moved");
        var updated = await admin.PutJsonAsync($"/api/admin/users/{id}/email", new { email = next });

        Assert.Equal(next, updated.GetProperty("email").GetString());

        // …and it stuck: reading the user back shows the new address, not a cached echo.
        var read = await admin.GetJsonAsync($"/api/users/{id}");
        Assert.Equal(next, read.GetProperty("email").GetString());
    }

    /// <summary>
    /// The normalized column is what sign-in-by-email looks up. If it is left stale the account
    /// quietly answers to its OLD address and not its new one — the worst kind of bug, because the
    /// change appears to have worked everywhere you look.
    /// </summary>
    [Fact]
    public async Task The_user_can_sign_in_with_the_new_address()
    {
        var admin = await _factory.LoginAdminAsync();

        var email = Unique("signin");
        var userName = $"signin{Guid.NewGuid():N}"[..16];
        const string password = "Sign!nTest123";
        var created = await admin.PostJsonAsync("/api/auth/register", new
        {
            email, userName, password, roles = new[] { "Fronter" },
        });
        var id = created.GetProperty("id").GetGuid();

        var next = Unique("signin-new");
        await admin.PutJsonAsync($"/api/admin/users/{id}/email", new { email = next });

        var anon = _factory.CreateClient();
        var login = await anon.PostAsJsonAsync("/api/auth/login",
            new { userNameOrEmail = next, password });

        // Not Unauthorized: the account is reachable by the address it now has.
        Assert.NotEqual(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    /// <summary>Two accounts sharing an address would race for the same password-reset inbox.</summary>
    [Fact]
    public async Task An_address_already_in_use_is_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (first, firstEmail) = await CreateAgentAsync(admin);
        var (second, _) = await CreateAgentAsync(admin);

        var res = await admin.PutAsJsonAsync($"/api/admin/users/{second}/email", new { email = firstEmail });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // The refused change left the first account alone.
        var read = await admin.GetJsonAsync($"/api/users/{first}");
        Assert.Equal(firstEmail, read.GetProperty("email").GetString());
    }

    [Fact]
    public async Task A_malformed_address_is_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, before) = await CreateAgentAsync(admin);

        var res = await admin.PutAsJsonAsync($"/api/admin/users/{id}/email", new { email = "not-an-address" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var read = await admin.GetJsonAsync($"/api/users/{id}");
        Assert.Equal(before, read.GetProperty("email").GetString());
    }

    [Fact]
    public async Task An_empty_address_is_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, _) = await CreateAgentAsync(admin);

        var res = await admin.PutAsJsonAsync($"/api/admin/users/{id}/email", new { email = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>Nothing changed, so nothing should be announced or invalidated.</summary>
    [Fact]
    public async Task Setting_the_same_address_again_is_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, current) = await CreateAgentAsync(admin);

        var res = await admin.PutAsJsonAsync($"/api/admin/users/{id}/email", new { email = current });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    /// <summary>
    /// THE authorization property. Without a permission gate this endpoint is an account-takeover
    /// tool: point it at an administrator, then use "forgot password" on the address you just set.
    /// </summary>
    [Fact]
    public async Task An_ordinary_agent_cannot_change_anyones_email()
    {
        var admin = await _factory.LoginAdminAsync();
        var (victim, victimEmail) = await CreateAgentAsync(admin);

        // A plain agent with a real session — no users.manage grant anywhere in sight.
        var agentName = $"plain{Guid.NewGuid():N}"[..16];
        const string agentPassword = "PlainAgent!123";
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = Unique("plain"), userName = agentName, password = agentPassword,
            roles = new[] { "Fronter" },
        });
        var agent = await _factory.LoginAsync(agentName, agentPassword);

        var res = await agent.PutAsJsonAsync($"/api/admin/users/{victim}/email",
            new { email = Unique("attacker") });

        Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"an agent must not be able to reassign an account's email; got {res.StatusCode}");

        var read = await admin.GetJsonAsync($"/api/users/{victim}");
        Assert.Equal(victimEmail, read.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Signed_out_callers_are_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, _) = await CreateAgentAsync(admin);

        var anon = _factory.CreateClient();
        var res = await anon.PutAsJsonAsync($"/api/admin/users/{id}/email", new { email = Unique("anon") });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        var admin = await _factory.LoginAdminAsync();
        var res = await admin.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/email",
            new { email = Unique("ghost") });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>The change has to be findable afterwards, by both addresses.</summary>
    [Fact]
    public async Task The_change_is_written_to_the_audit_log()
    {
        var admin = await _factory.LoginAdminAsync();
        var (id, before) = await CreateAgentAsync(admin);
        var next = Unique("audited");

        await admin.PutJsonAsync($"/api/admin/users/{id}/email", new { email = next });

        var audit = await admin.GetJsonAsync("/api/admin/audit?action=EmailChanged");
        var rows = audit.TryGetProperty("items", out var items) ? items : audit;

        var mine = rows.EnumerateArray()
            .Where(a => a.GetProperty("entityId").GetString() == id.ToString())
            .ToList();

        Assert.NotEmpty(mine);
        var changes = mine[0].GetProperty("changes").GetString() ?? "";
        Assert.Contains(before, changes);
        Assert.Contains(next, changes);
    }
}
