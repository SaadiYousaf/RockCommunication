using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class AdminFlowTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public AdminFlowTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task IpAllowlist_crud_works()
    {
        // The IP allowlist is a platform-level control — SuperAdmin only. A plain agency admin
        // must NOT be able to touch it (see DbSeeder superAdminOnlyPerms / restricted).
        var admin = await _factory.LoginAdminAsync();
        var forbidden = await admin.PostAsJsonAsync("/api/admin/ip-allowlist", new { cidrOrIp = "10.0.0.0/24", note = "office" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbidden.StatusCode);

        var sa = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");

        var added = await sa.PostJsonAsync("/api/admin/ip-allowlist", new { cidrOrIp = "10.0.0.0/24", note = "office" });
        var id = added.GetProperty("id").GetGuid();

        var list = await sa.GetJsonAsync("/api/admin/ip-allowlist");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetGuid() == id);

        var del = await sa.DeleteAsync($"/api/admin/ip-allowlist/{id}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Verticals_crud_works()
    {
        var admin = await _factory.LoginAdminAsync();
        var created = await admin.PostJsonAsync("/api/admin/verticals", new { name = "Health" });
        var id = created.GetProperty("id").GetGuid();
        var list = await admin.GetJsonAsync("/api/admin/verticals");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Commission_config_upsert_round_trips()
    {
        var admin = await _factory.LoginAdminAsync();
        await admin.PutAsJsonAsync("/api/admin/commission-config", new
        {
            ruleName = "closer-flat-rate", amount = 100m, threshold = (decimal?)null, enabled = true
        });
        var list = await admin.GetJsonAsync("/api/admin/commission-config");
        var entry = list.EnumerateArray().First(e => e.GetProperty("ruleName").GetString() == "closer-flat-rate");
        Assert.Equal(100m, entry.GetProperty("amount").GetDecimal());
    }
}
