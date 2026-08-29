using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Disabling an agency (or call center) from the SuperAdmin panel must lock every
/// user underneath it out of login — even though their own account stays IsActive.
/// Runs in its own class so its (isolated, per-class) DB can disable the Default
/// Agency without affecting the rest of the suite.
/// </summary>
public class TenantKillSwitchTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public TenantKillSwitchTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Disabling_agency_blocks_its_users_login_but_not_superadmin()
    {
        // admin belongs to the Default Agency and can sign in to start with.
        var before = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { userNameOrEmail = "admin", password = "Admin@123!" });
        before.EnsureSuccessStatusCode();

        // SuperAdmin (no agency) disables the Default Agency.
        var superadmin = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");
        var agencies = await superadmin.GetJsonAsync("/api/agencies?includeInactive=true");
        var def = agencies.EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == "Default Agency");
        var id = def.GetProperty("id").GetString();
        var code = def.TryGetProperty("code", out var c) && c.ValueKind != JsonValueKind.Null
            ? c.GetString() : null;

        var disable = await superadmin.PutAsJsonAsync(
            $"/api/agencies/{id}", new { name = "Default Agency", code, isActive = false });
        disable.EnsureSuccessStatusCode();

        // A user of the now-disabled agency can no longer log in.
        var blocked = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { userNameOrEmail = "admin", password = "Admin@123!" });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // The platform SuperAdmin is never gated — recovery path stays open.
        var sa = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { userNameOrEmail = "superadmin", password = "SuperAdmin@123!" });
        sa.EnsureSuccessStatusCode();

        // Re-enabling the agency restores login for its users.
        var enable = await superadmin.PutAsJsonAsync(
            $"/api/agencies/{id}", new { name = "Default Agency", code, isActive = true });
        enable.EnsureSuccessStatusCode();

        var restored = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { userNameOrEmail = "admin", password = "Admin@123!" });
        restored.EnsureSuccessStatusCode();
    }

    /// <summary>Finds the Default Agency and returns (id, code) for the disable/enable calls.</summary>
    private static async Task<(string Id, string? Code)> DefaultAgencyAsync(HttpClient superadmin)
    {
        var agencies = await superadmin.GetJsonAsync("/api/agencies?includeInactive=true");
        var def = agencies.EnumerateArray().First(a => a.GetProperty("name").GetString() == "Default Agency");
        var code = def.TryGetProperty("code", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;
        return (def.GetProperty("id").GetString()!, code);
    }

    private static Task<HttpResponseMessage> SetActiveAsync(HttpClient superadmin, string agencyId, bool active)
        => superadmin.PutAsJsonAsync($"/api/agencies/{agencyId}/active", new { isActive = active });

    /// <summary>
    /// THE CASCADE. Disabling an agency must switch off its call centres and its users, not just its
    /// own flag. Before this existed, production held 6 disabled agencies that still owned 13 active
    /// call centres and 41 active accounts.
    /// </summary>
    [Fact]
    public async Task Disabling_agency_cascades_to_call_centers_and_users()
    {
        var superadmin = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");
        var (id, _) = await DefaultAgencyAsync(superadmin);

        // The impact preview must report real counts BEFORE anything is changed — it is what the
        // confirmation dialog shows, so a wrong number here misleads the operator.
        var impact = await superadmin.GetJsonAsync($"/api/agencies/{id}/disable-impact");
        var centresBefore = impact.GetProperty("callCenters").GetInt32();
        var usersBefore = impact.GetProperty("users").GetInt32();
        Assert.True(usersBefore > 0, "the seeded Default Agency should own at least one active user");

        var disable = await SetActiveAsync(superadmin, id, false);
        disable.EnsureSuccessStatusCode();
        var result = JsonDocument.Parse(await disable.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(centresBefore, result.GetProperty("callCentersChanged").GetInt32());
        Assert.Equal(usersBefore, result.GetProperty("usersChanged").GetInt32());

        // Nothing under the agency is left active.
        var after = await superadmin.GetJsonAsync($"/api/agencies/{id}/disable-impact");
        Assert.Equal(0, after.GetProperty("callCenters").GetInt32());
        Assert.Equal(0, after.GetProperty("users").GetInt32());

        await SetActiveAsync(superadmin, id, true);
    }

    /// <summary>
    /// TEST 5 from the brief, and the reason this work exists: a token minted BEFORE the disable must
    /// stop working immediately, not when it expires 15 minutes later.
    /// </summary>
    [Fact]
    public async Task Access_token_issued_before_disable_is_rejected_immediately()
    {
        // A live, authenticated session that works right now.
        var victim = await _factory.LoginAsync("admin", "Admin@123!");
        (await victim.GetAsync("/api/leads")).EnsureSuccessStatusCode();

        var superadmin = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");
        var (id, _) = await DefaultAgencyAsync(superadmin);
        (await SetActiveAsync(superadmin, id, false)).EnsureSuccessStatusCode();

        // Same client, same unexpired token. Asserting on the REQUEST being refused, never on
        // expiry — the test factory issues 60-minute tokens, so expiry would prove nothing.
        var blocked = await victim.GetAsync("/api/leads");
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Contains("tenant_disabled", blocked.Headers.WwwAuthenticate.ToString());

        await SetActiveAsync(superadmin, id, true);
    }

    /// <summary>
    /// Re-enabling restores exactly what the cascade took. A user an admin disabled INDIVIDUALLY
    /// before the shutdown must stay disabled — otherwise re-enabling an agency quietly hands access
    /// back to someone deliberately blocked.
    /// </summary>
    [Fact]
    public async Task Enabling_restores_only_what_the_cascade_disabled()
    {
        var superadmin = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");
        var (id, _) = await DefaultAgencyAsync(superadmin);

        // Pick an ordinary user of the agency and disable them on their own.
        var users = await superadmin.GetJsonAsync($"/api/users?agencyId={id}");
        var victim = users.EnumerateArray().First(u =>
            u.GetProperty("userName").GetString() != "admin" &&
            !u.GetProperty("roles").EnumerateArray().Any(r => r.GetString() == "SuperAdmin"));
        var victimId = victim.GetProperty("id").GetString();

        (await superadmin.PutAsJsonAsync($"/api/admin/users/{victimId}/active", new { isActive = false }))
            .EnsureSuccessStatusCode();

        (await SetActiveAsync(superadmin, id, false)).EnsureSuccessStatusCode();
        (await SetActiveAsync(superadmin, id, true)).EnsureSuccessStatusCode();

        var after = await superadmin.GetJsonAsync($"/api/users?agencyId={id}");
        var restored = after.EnumerateArray().First(u => u.GetProperty("id").GetString() == victimId);
        Assert.False(restored.GetProperty("isActive").GetBoolean(),
            "an individually-disabled user must NOT be reactivated by re-enabling their agency");

        // …while the rest of the agency is back.
        (await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { userNameOrEmail = "admin", password = "Admin@123!" }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>Disabling twice must converge, not double-apply or fail.</summary>
    [Fact]
    public async Task Disabling_twice_is_idempotent()
    {
        var superadmin = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");
        var (id, _) = await DefaultAgencyAsync(superadmin);

        (await SetActiveAsync(superadmin, id, false)).EnsureSuccessStatusCode();

        var second = await SetActiveAsync(superadmin, id, false);
        second.EnsureSuccessStatusCode();
        var result = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        // Everything was already off, so the second run changes nothing.
        Assert.Equal(0, result.GetProperty("callCentersChanged").GetInt32());
        Assert.Equal(0, result.GetProperty("usersChanged").GetInt32());

        await SetActiveAsync(superadmin, id, true);
    }

    /// <summary>
    /// An admin must not be able to re-enable an individual while their tenant is still disabled —
    /// the list would show them Active while the gate keeps refusing them.
    /// </summary>
    [Fact]
    public async Task Cannot_enable_a_user_while_their_agency_is_disabled()
    {
        var superadmin = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");
        var (id, _) = await DefaultAgencyAsync(superadmin);

        var users = await superadmin.GetJsonAsync($"/api/users?agencyId={id}");
        var target = users.EnumerateArray().First(u =>
            !u.GetProperty("roles").EnumerateArray().Any(r => r.GetString() == "SuperAdmin"));
        var targetId = target.GetProperty("id").GetString();

        (await SetActiveAsync(superadmin, id, false)).EnsureSuccessStatusCode();

        var attempt = await superadmin.PutAsJsonAsync(
            $"/api/admin/users/{targetId}/active", new { isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, attempt.StatusCode);

        await SetActiveAsync(superadmin, id, true);
    }
}
