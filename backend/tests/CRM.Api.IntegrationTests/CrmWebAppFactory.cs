using CRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CRM.Api.IntegrationTests;

public class CrmWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbFile = $"crm-tests-{Guid.NewGuid():N}.db";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbFile}",
                ["Database:Provider"] = "Sqlite",
                ["Jwt:Issuer"] = "CRM",
                ["Jwt:Audience"] = "CRM",
                ["Jwt:Secret"] = "test-secret-must-be-at-least-32-characters-long-for-hmac-sha256-tests",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Cors:Origins:0"] = "http://localhost",
                // Force stub integrations so tests never make real network calls
                // (the Development config points email at a live SMTP host).
                ["Integrations:Email:Provider"] = "Stub",
                ["Integrations:Email:SmtpHost"] = "",
                ["Integrations:Sms:Provider"] = "Stub",
                ["Integrations:Dialer:Provider"] = "Vici",
                ["Integrations:Dialer:BaseUrl"] = "",
                ["Integrations:Lyons:Provider"] = "Stub",
                ["Ai:Provider"] = "Stub",
                // Effectively disable rate limiting for the test suite — many tests
                // register/login in quick succession and would otherwise trip the strict
                // auth bucket (5/min). Production limits are unaffected.
                ["RateLimits:Auth:PerMinute"] = "1000000",
                ["RateLimits:Webhook:PerMinute"] = "1000000",
                ["RateLimits:Anon:Burst"] = "1000000",
                ["RateLimits:User:Burst"] = "1000000",
                // Configure the webhook HMAC secret so the dialer/inbound endpoints are
                // "configured" (not fail-closed 503); tests sign with TestHelpers.WebhookSecret.
                ["Webhooks:Dialer:Secret"] = "test-webhook-secret-0123456789abcdef",
                ["Webhooks:Inbound:Secret"] = "test-webhook-secret-0123456789abcdef",
                // Tests can't complete a TOTP enrolment; verify the gate separately.
                ["Security:EnforceMandatoryTwoFactor"] = "false",
            });

            // appsettings.json also declares the platform's approved NETWORKS, which are seeded on
            // every start. A test host must not inherit them, or "an empty allowlist" silently
            // becomes "whatever the real platform happens to allow" — which is how the allowlist
            // tests started failing the moment a real office was added to config. Indices, because
            // an array cannot be unset through in-memory configuration.
            var clearNetworks = new Dictionary<string, string?>();
            for (var i = 0; i < 10; i++)
            {
                clearNetworks[$"Security:IpAllowlist:{i}:CidrOrIp"] = "";
                clearNetworks[$"Security:IpAllowlist:{i}:Note"] = "";
            }
            config.AddInMemoryCollection(clearNetworks);
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // The database AND its write-ahead-log sidecars.
        //
        // SQLite in WAL mode keeps "<name>-wal" and "<name>-shm" beside the database, and deleting
        // only the database leaves them behind. Every test class creates its own file, so each run
        // of the suite stranded a few megabytes of -wal; left alone since August they had grown to
        // 3,646 files and 4.6 GB inside bin/, which is also why searching this tree had become so
        // slow. Cleaning up after ourselves is the fix — nothing here is wanted once the host is
        // gone, and the directory is a build output anyway.
        foreach (var path in new[] { _dbFile, _dbFile + "-wal", _dbFile + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* a locked file is not worth failing a test run over */ }
        }
    }
}
