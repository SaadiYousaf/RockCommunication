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
}
