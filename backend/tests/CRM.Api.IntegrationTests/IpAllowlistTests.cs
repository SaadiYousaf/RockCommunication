using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The network allowlist: the application is reachable only from approved addresses.
///
/// The failure mode being guarded against is not "someone got in" — it is "nobody can get in". One
/// wrong entry here locks every person out of a live platform, so most of these tests are about the
/// ways in must stay open: SuperAdmin from anywhere, sign-in from anywhere, machine callers from
/// anywhere, and everything from everywhere while the list is empty.
/// </summary>
public class IpAllowlistTests : IClassFixture<IpAllowlistTests.ProxiedFactory>
{
    private readonly ProxiedFactory _factory;
    public IpAllowlistTests(ProxiedFactory factory) => _factory = factory;

    /// <summary>
    /// A host that presents as if it sits behind our reverse proxy.
    ///
    /// The test server has no real socket, so <c>Connection.RemoteIpAddress</c> is null — and
    /// ClientIp only trusts forwarding headers when the immediate peer is a trusted proxy, which
    /// null is not. Every request would therefore resolve to no address at all and sail past the
    /// allowlist, testing nothing. Stamping loopback on the connection reproduces production, where
    /// nginx is the peer and the forwarded header carries the real client.
    /// </summary>
    public sealed class ProxiedFactory : CrmWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, LoopbackPeerFilter>());
        }

        private sealed class LoopbackPeerFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (ctx, nextStep) =>
                {
                    ctx.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                    await nextStep();
                });
                next(app);
            };
        }
    }

    /// <summary>
    /// A request that looks like it came from the public internet.
    ///
    /// The test host connects over loopback, which the middleware waves through — the proxy in front
    /// of production always presents as 127.0.0.1, so loopback cannot be treated as suspicious. The
    /// forwarded header is what the middleware actually reads, so setting it is what makes a test
    /// request stand in for a real remote one.
    /// </summary>
    private static void From(HttpClient client, string ip)
    {
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
    }

    private static async Task<Guid> AllowAsync(HttpClient superAdmin, string cidrOrIp)
    {
        var added = await superAdmin.PostJsonAsync("/api/admin/ip-allowlist",
            new { cidrOrIp, note = "added by a test" });
        return added.GetProperty("id").GetGuid();
    }

    private static async Task RevokeAsync(HttpClient superAdmin, Guid id)
        => (await superAdmin.DeleteAsync($"/api/admin/ip-allowlist/{id}")).EnsureSuccessStatusCode();

    /// <summary>Deploying the feature must change nothing until somebody opts in.</summary>
    [Fact]
    public async Task An_empty_allowlist_allows_everyone()
    {
        var admin = await _factory.LoginAdminAsync();
        From(admin, "203.0.113.50");

        var res = await admin.GetAsync("/api/users");
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_user_outside_the_allowlist_is_refused_with_an_explanation()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var entry = await AllowAsync(superAdmin, "198.51.100.0/24");
        try
        {
            var admin = await _factory.LoginAdminAsync();
            From(admin, "203.0.113.50");          // not in 198.51.100.0/24

            var res = await admin.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

            // The message has to name the address, or the user cannot tell their administrator
            // which one to approve.
            var body = await res.Content.ReadAsStringAsync();
            Assert.Contains("ip_not_allowed", body);
            Assert.Contains("203.0.113.50", body);
        }
        finally { await RevokeAsync(superAdmin, entry); }
    }

    [Fact]
    public async Task A_user_inside_the_allowlist_is_allowed()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var entry = await AllowAsync(superAdmin, "198.51.100.0/24");
        try
        {
            var admin = await _factory.LoginAdminAsync();
            From(admin, "198.51.100.77");          // inside the range

            var res = await admin.GetAsync("/api/users");
            Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        }
        finally { await RevokeAsync(superAdmin, entry); }
    }

    [Fact]
    public async Task A_single_address_entry_matches_exactly_that_address()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var entry = await AllowAsync(superAdmin, "198.51.100.10");
        try
        {
            var admin = await _factory.LoginAdminAsync();

            From(admin, "198.51.100.10");
            Assert.NotEqual(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/users")).StatusCode);

            From(admin, "198.51.100.11");
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/users")).StatusCode);
        }
        finally { await RevokeAsync(superAdmin, entry); }
    }

    /// <summary>
    /// THE property that makes this recoverable. Without it, one typo in a CIDR permanently locks
    /// every person out of the platform, including whoever would fix it.
    /// </summary>
    [Fact]
    public async Task SuperAdmin_is_never_restricted_by_the_allowlist()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var entry = await AllowAsync(superAdmin, "198.51.100.0/24");
        try
        {
            // Plainly outside the approved range.
            From(superAdmin, "203.0.113.99");

            Assert.NotEqual(HttpStatusCode.Forbidden, (await superAdmin.GetAsync("/api/users")).StatusCode);

            // …including the allowlist screen itself, which is the one they need to fix a mistake.
            Assert.NotEqual(HttpStatusCode.Forbidden, (await superAdmin.GetAsync("/api/admin/ip-allowlist")).StatusCode);
        }
        finally { await RevokeAsync(superAdmin, entry); }
    }

    /// <summary>
    /// Sign-in stays open because identity is not known until it completes — closing it would stop
    /// the exempt SuperAdmin from ever authenticating. A blocked user signs in and is then refused
    /// on their first real request, which is where the explanation is.
    /// </summary>
    [Fact]
    public async Task Signing_in_stays_reachable_from_any_address()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var entry = await AllowAsync(superAdmin, "198.51.100.0/24");
        try
        {
            var anon = _factory.CreateClient();
            From(anon, "203.0.113.50");

            var res = await anon.PostAsJsonAsync("/api/auth/login",
                new { userNameOrEmail = "admin", password = "Admin@123!" });

            Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        }
        finally { await RevokeAsync(superAdmin, entry); }
    }

    /// <summary>
    /// Carriers and dialers call from addresses nobody can enumerate ahead of time, and prove
    /// themselves with a signature instead. Pinning them to the list would silently stop intake.
    /// </summary>
    [Fact]
    public async Task Machine_callers_are_not_restricted()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var entry = await AllowAsync(superAdmin, "198.51.100.0/24");
        try
        {
            var anon = _factory.CreateClient();
            From(anon, "203.0.113.50");

            // Rejected on its own merits (no signature), but never by the allowlist.
            var res = await anon.PostAsJsonAsync("/api/webhooks/dialer", new { });
            Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
        }
        finally { await RevokeAsync(superAdmin, entry); }
    }

    /// <summary>Revoking an address has to bite immediately, not when a cache feels like it.</summary>
    [Fact]
    public async Task Adding_and_revoking_takes_effect_at_once()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var admin = await _factory.LoginAdminAsync();
        From(admin, "203.0.113.50");

        Assert.NotEqual(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/users")).StatusCode);

        var entry = await AllowAsync(superAdmin, "198.51.100.0/24");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/users")).StatusCode);

        await RevokeAsync(superAdmin, entry);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/users")).StatusCode);
    }

    /// <summary>Only SuperAdmin decides which networks are approved.</summary>
    [Fact]
    public async Task An_ordinary_admin_cannot_change_the_allowlist()
    {
        var admin = await _factory.LoginAdminAsync();
        var res = await admin.PostAsJsonAsync("/api/admin/ip-allowlist",
            new { cidrOrIp = "203.0.113.0/24", note = "should not be allowed" });

        Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"only SuperAdmin may edit the network allowlist; got {res.StatusCode}");
    }
}
