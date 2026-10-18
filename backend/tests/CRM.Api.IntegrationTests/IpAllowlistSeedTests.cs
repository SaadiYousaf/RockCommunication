using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Networks declared in configuration rather than typed into the admin screen.
///
/// The allowlist is otherwise editable only by a signed-in SuperAdmin, which is no help at the one
/// moment it matters — when a wrong entry has locked everybody out and the person who would fix it
/// cannot reach the application to do so. Declaring a network in config puts it back with a deploy.
/// </summary>
public class IpAllowlistSeedTests : IClassFixture<IpAllowlistSeedTests.SeededFactory>
{
    private readonly SeededFactory _factory;
    public IpAllowlistSeedTests(SeededFactory factory) => _factory = factory;

    private const string Prefix = "2001:db8:1234:5678::/64";

    /// <summary>
    /// A host whose configuration declares networks, and which presents as sitting behind our
    /// reverse proxy so the forwarded header is trusted (see IpAllowlistTests for why).
    /// </summary>
    public sealed class SeededFactory : CrmWebAppFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // APP configuration, not host configuration. The base factory clears
            // Security:IpAllowlist in host config so no test inherits the real platform's networks,
            // and app configuration is layered on top of host configuration — so declaring them
            // here wins, whichever order the two factories happen to register in.
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:IpAllowlist:0:CidrOrIp"] = Prefix,
                    ["Security:IpAllowlist:0:Note"] = "Declared in configuration",
                    // The SAME network written in upper case. IPv6 is written either way, so this
                    // must not become a second entry for the same range.
                    ["Security:IpAllowlist:1:CidrOrIp"] = "2001:DB8:1234:5678::/64",
                    ["Security:IpAllowlist:1:Note"] = "Duplicate, differently cased",
                    // Nonsense. It could never match a client, so it must not be stored looking as
                    // though it had been approved.
                    ["Security:IpAllowlist:2:CidrOrIp"] = "not-an-address",
                    ["Security:IpAllowlist:2:Note"] = "Typo",
                });
            });
            return base.CreateHost(builder);
        }

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

    private static void From(HttpClient client, string ip)
    {
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
    }

    /// <summary>The declared network is on the list after start — once, not twice.</summary>
    [Fact]
    public async Task A_declared_network_is_on_the_list_exactly_once()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var list = await superAdmin.GetJsonAsync("/api/admin/ip-allowlist");

        var matching = list.EnumerateArray()
            .Count(e => string.Equals(e.GetProperty("cidrOrIp").GetString(), Prefix,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, matching);
    }

    /// <summary>
    /// And it actually admits somebody. A row in a table that does not let anyone in would be the
    /// worst outcome here — it reads as fixed while the lockout continues.
    /// </summary>
    [Fact]
    public async Task A_client_on_the_declared_network_is_admitted()
    {
        var admin = await _factory.LoginAdminAsync();
        From(admin, "2001:db8:1234:5678:aaaa:bbbb:cccc:dddd");

        var res = await admin.GetAsync("/api/users");
        Assert.NotEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    /// <summary>
    /// …without opening the door to everyone. If a declared entry quietly disabled the allowlist,
    /// every one of these tests above would still pass.
    /// </summary>
    [Fact]
    public async Task A_client_on_another_network_is_still_refused()
    {
        var admin = await _factory.LoginAdminAsync();
        From(admin, "2001:db8:9999:9999::1");

        var res = await admin.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    /// <summary>A malformed entry is refused rather than stored as an approved-looking row.</summary>
    [Fact]
    public async Task A_malformed_entry_is_not_stored()
    {
        var superAdmin = await _factory.LoginSuperAdminAsync();
        var list = await superAdmin.GetJsonAsync("/api/admin/ip-allowlist");

        Assert.DoesNotContain(list.EnumerateArray(),
            e => (e.GetProperty("cidrOrIp").GetString() ?? "").Contains("not-an-address"));
    }
}
