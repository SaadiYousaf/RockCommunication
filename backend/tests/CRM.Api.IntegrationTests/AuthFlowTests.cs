using System.Net;
using System.Net.Http.Json;

namespace CRM.Api.IntegrationTests;

public class AuthFlowTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public AuthFlowTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_200()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Login_with_seeded_admin_succeeds()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = "admin", password = "Admin@123!" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(body!.RequiresTwoFactor);
        Assert.NotNull(body.User);
        Assert.Contains("Admin", body.User!.Roles);
    }

    [Fact]
    public async Task Unauthenticated_request_to_leads_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/leads");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Authenticated_me_returns_user_summary()
    {
        var client = await _factory.LoginAdminAsync();
        var me = await client.GetFromJsonAsync<UserSummary>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Equal("admin", me!.UserName);
    }
}

internal record LoginResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, bool RequiresTwoFactor, string? TwoFactorToken, UserSummary? User);
internal record UserSummary(Guid Id, string UserName, string Email, Guid AgencyId, string[] Roles);
