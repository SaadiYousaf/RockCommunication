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
            });
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
        try { if (File.Exists(_dbFile)) File.Delete(_dbFile); } catch { }
    }
}
