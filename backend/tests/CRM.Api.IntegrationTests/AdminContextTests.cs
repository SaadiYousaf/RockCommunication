using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Admin context picker — POST /api/auth/context re-scopes the session to a chosen call center,
/// reflects it in the returned summary, and keeps the scope across a token refresh.
/// </summary>
public class AdminContextTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public AdminContextTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Context_scopes_to_a_call_center_and_survives_refresh()
    {
        var admin = await _factory.LoginAdminAsync();
        var ccName = $"Ctx-{Guid.NewGuid():N}".Substring(0, 16);
        var cc = await admin.PostJsonAsync("/api/admin/call-centers", new
        {
            name = ccName, code = (string?)null, adminName = "CC Admin", adminEmail = $"cc-{Guid.NewGuid():N}@crm.local"
        });
        var ccId = cc.GetProperty("id").GetGuid();

        var ctx = await admin.PostJsonAsync("/api/auth/context", new { callCenterId = ccId });
        var user = ctx.GetProperty("user");
        Assert.Equal(ccId, user.GetProperty("callCenterId").GetGuid());
        Assert.Equal(ccName, user.GetProperty("callCenterName").GetString());

        var access = ctx.GetProperty("accessToken").GetString()!;
        var refresh = ctx.GetProperty("refreshToken").GetString()!;
        Assert.Equal(ccId.ToString(), JwtClaim(access, "callcenter"));   // token carries the chosen scope

        // Durability: refreshing the scoped session keeps the call-center scope (it isn't reverted
        // to the admin's home scope of "agency-wide").
        var refreshResp = await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        refreshResp.EnsureSuccessStatusCode();
        var refreshed = JsonDocument.Parse(await refreshResp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ccId.ToString(), JwtClaim(refreshed.GetProperty("accessToken").GetString()!, "callcenter"));
    }

    [Fact]
    public async Task Context_all_call_centers_clears_the_scope()
    {
        var admin = await _factory.LoginAdminAsync();
        var ctx = await admin.PostJsonAsync("/api/auth/context", new { });
        Assert.Equal(JsonValueKind.Null, ctx.GetProperty("user").GetProperty("callCenterId").ValueKind);
        Assert.Null(JwtClaim(ctx.GetProperty("accessToken").GetString()!, "callcenter"));  // no claim = agency-wide
    }

    [Fact]
    public async Task Context_rejects_an_unknown_call_center()
    {
        var admin = await _factory.LoginAdminAsync();
        var resp = await admin.PostAsJsonAsync("/api/auth/context", new { callCenterId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    /// <summary>Decode a JWT payload and read a claim (no signature check — test-only).</summary>
    private static string? JwtClaim(string accessToken, string claim)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = (payload.Length % 4) switch { 2 => payload + "==", 3 => payload + "=", _ => payload };
        using var doc = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return doc.RootElement.TryGetProperty(claim, out var v) ? v.GetString() : null;
    }
}
