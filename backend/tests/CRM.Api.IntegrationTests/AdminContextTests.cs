using System.Net;
using System.Net.Http.Headers;
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

    [Fact]
    public async Task Scoped_superadmin_only_sees_the_chosen_agency_users()
    {
        var sa = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");

        var beaEmail = $"bea-{Guid.NewGuid():N}@crm.local";
        var cidEmail = $"cid-{Guid.NewGuid():N}@crm.local";
        var b = await sa.PostJsonAsync("/api/agencies", new
        { name = $"B-{Guid.NewGuid():N}".Substring(0, 10), code = (string?)null, ceoName = "Bea Ceo", ceoEmail = beaEmail });
        await sa.PostJsonAsync("/api/agencies", new
        { name = $"C-{Guid.NewGuid():N}".Substring(0, 10), code = (string?)null, ceoName = "Cid Ceo", ceoEmail = cidEmail });
        var agencyBId = b.GetProperty("id").GetGuid();

        // Baseline: an UNSCOPED SuperAdmin sees users across every agency (platform view preserved).
        var all = await sa.GetJsonAsync("/api/users");
        Assert.Contains(all.EnumerateArray(), u => u.GetProperty("email").GetString() == beaEmail);
        Assert.Contains(all.EnumerateArray(), u => u.GetProperty("email").GetString() == cidEmail);

        // Scope to agency B, then the SAME SuperAdmin only sees agency B's users.
        var ctx = await sa.PostJsonAsync("/api/auth/context", new { agencyId = agencyBId });
        var scoped = _factory.CreateClient();
        scoped.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ctx.GetProperty("accessToken").GetString());

        var scopedUsers = await scoped.GetJsonAsync("/api/users");
        Assert.Contains(scopedUsers.EnumerateArray(), u => u.GetProperty("email").GetString() == beaEmail);
        Assert.DoesNotContain(scopedUsers.EnumerateArray(), u => u.GetProperty("email").GetString() == cidEmail);
    }

    /// <summary>
    /// A SuperAdmin who has NOT picked a working context must still be able to list sales.
    ///
    /// REGRESSION: ListSales used to throw ForbiddenAccessException("An agency must be specified.")
    /// whenever a SuperAdmin omitted ?agencyId. A list page cannot render a 403, so the client fell
    /// through to its "no sales found" empty state — the Sales page looked EMPTY while the dashboard
    /// (which does pass an agencyId) reported real totals off the very same rows. That reads as data
    /// loss to whoever is looking at it.
    /// </summary>
    [Fact]
    public async Task Unscoped_superadmin_can_list_sales_without_naming_an_agency()
    {
        var sa = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");

        var resp = await sa.GetAsync("/api/sales");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>
    /// The other half of the contract: once a SuperAdmin picks a context, the sales list narrows to
    /// that agency rather than continuing to show the platform.
    /// </summary>
    [Fact]
    public async Task Scoped_superadmin_sees_only_the_chosen_agency_sales()
    {
        var sa = await _factory.LoginAsync("superadmin", "SuperAdmin@123!");

        var made = await sa.PostJsonAsync("/api/agencies", new
        {
            name = $"S-{Guid.NewGuid():N}".Substring(0, 10),
            code = (string?)null,
            ceoName = "Sal Ceo",
            ceoEmail = $"sal-{Guid.NewGuid():N}@crm.local",
        });
        var freshAgencyId = made.GetProperty("id").GetGuid();

        var ctx = await sa.PostJsonAsync("/api/auth/context", new { agencyId = freshAgencyId });
        var scoped = _factory.CreateClient();
        scoped.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ctx.GetProperty("accessToken").GetString());

        var body = await scoped.GetJsonAsync("/api/sales");

        // A brand-new agency has no sales, so the scoped view must be empty even though the
        // platform-wide view (asserted above) succeeds. This is the assertion that would fail if the
        // "no agency predicate" branch ever leaked across tenants.
        Assert.Equal(0, body.GetProperty("total").GetInt32());
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
