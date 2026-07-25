using System.Net;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Locks in the anti-privilege-escalation guard: a plain <c>users.manage</c> holder that is NOT
/// SuperAdmin/Admin/CEO (here a ProgramManager) must not be able to hand out an agency-admin-equivalent
/// role — neither by updating an existing user's roles NOR by creating a new user via register.
/// </summary>
public class PrivilegeEscalationTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public PrivilegeEscalationTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProgramManager_cannot_escalate_roles_via_update_or_register()
    {
        var admin = await _factory.LoginAdminAsync();
        var me = await admin.GetJsonAsync("/api/auth/me");
        var agencyId = me.GetProperty("agencyId").GetGuid();

        // A ProgramManager: holds users.manage but is NOT Admin/CEO/SuperAdmin.
        var pmName = $"pm{Guid.NewGuid():N}".Substring(0, 12);
        await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{pmName}@crm.local", userName = pmName,
            password = "Pm@12345!", agencyId, roles = new[] { "ProgramManager" }
        });
        var pm = await _factory.LoginAsync(pmName, "Pm@12345!");

        // A target user in the same agency.
        var tgtName = $"tgt{Guid.NewGuid():N}".Substring(0, 12);
        var tgt = await admin.PostJsonAsync("/api/auth/register", new
        {
            email = $"{tgtName}@crm.local", userName = tgtName,
            password = "Tgt@1234!", agencyId, roles = new[] { "Closer" }
        });
        var tgtId = tgt.GetProperty("id").GetGuid();

        // (1) UPDATE path: the ProgramManager may NOT escalate the target to Admin.
        var escalate = await pm.PutAsJsonAsync($"/api/admin/users/{tgtId}/roles",
            new { roles = new[] { "Closer", "Admin" } });
        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);

        // ...but may still assign a non-elevated role (guard must not over-block).
        var ok = await pm.PutAsJsonAsync($"/api/admin/users/{tgtId}/roles",
            new { roles = new[] { "Closer", "Verifier" } });
        Assert.True(ok.IsSuccessStatusCode, $"expected success assigning a non-elevated role, got {(int)ok.StatusCode}");

        // (2) CREATE path: the ProgramManager may NOT mint a brand-new Admin via register.
        var mintAdmin = await pm.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"newadmin-{Guid.NewGuid():N}@crm.local", userName = $"na{Guid.NewGuid():N}".Substring(0, 12),
            password = "Na@12345!", agencyId, roles = new[] { "Admin" }
        });
        Assert.Equal(HttpStatusCode.Forbidden, mintAdmin.StatusCode);
    }
}
