using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Locks in the boundary between public and protected endpoints.
///
/// The app runs a fail-closed FallbackPolicy: anything without an explicit auth attribute requires
/// an authenticated user. That is the right default — one forgotten [Authorize] would otherwise
/// publish an endpoint to the internet — but it also means a stray change to [AllowAnonymous] can
/// silently break SIGN-IN itself, or the health check the deploy smoke-test and uptime monitors
/// depend on. These tests fail loudly instead.
/// </summary>
public class AnonymousAccessTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public AnonymousAccessTests(CrmWebAppFactory factory) => _factory = factory;

    /// <summary>The endpoints a signed-OUT user must still be able to reach.</summary>
    public static TheoryData<string> PublicPosts() => new()
    {
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/api/auth/forgot-password",
        "/api/auth/reset-password",
        "/api/auth/email/confirm",
        "/api/auth/email/resend-confirmation",
        "/api/auth/2fa/verify",
    };

    [Theory]
    [MemberData(nameof(PublicPosts))]
    public async Task Public_auth_endpoints_are_reachable_without_a_token(string path)
    {
        var client = _factory.CreateClient();

        // Empty body on purpose: we assert only that AUTHORIZATION let us through. A 400/404/409 is
        // fine (the handler rejected the payload); a 401 means the endpoint stopped being public.
        var resp = await client.PostAsJsonAsync(path, new { });

        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_still_works_end_to_end_without_a_token()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "definitely-not-a-real-user",
            password = "definitely-not-the-password",
        });

        // Wrong credentials must be REJECTED BY THE HANDLER (401/403/400), never bounced by the
        // authorization layer before the handler is even reached.
        Assert.True(
            resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.BadRequest,
            $"login returned {(int)resp.StatusCode}");

        // And a REAL sign-in must still succeed — the check that actually proves the app is usable.
        var admin = await _factory.LoginAdminAsync();
        Assert.NotNull(admin);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_stay_public(string path)
    {
        // The deploy smoke test, Cloudflare and any uptime monitor hit these unauthenticated. If the
        // fallback policy ever swallows them, every one of those reports the app as DOWN.
        var resp = await _factory.CreateClient().GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Theory]
    [InlineData("/api/leads")]
    [InlineData("/api/users")]
    [InlineData("/api/commission-desk/sales")]
    [InlineData("/api/confidential/portal-credentials")]
    public async Task Protected_endpoints_reject_an_anonymous_caller(string path)
    {
        // The other half of the contract: fail-closed actually closes.
        var resp = await _factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
