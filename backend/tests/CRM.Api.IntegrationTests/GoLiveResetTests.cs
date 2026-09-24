using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using CRM.Infrastructure.Persistence;
using CRM.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The go-live reset: the one-time changeover that empties a demo installation and leaves exactly
/// one account behind.
///
/// This is the most destructive code in the product, so the tests are weighted towards the ways it
/// must REFUSE to run. A reset that fails to fire is an inconvenience; one that fires when it
/// shouldn't destroys a live agency's customer data.
/// </summary>
public class GoLiveResetTests : IDisposable
{
    private const string OwnerEmail = "owner-under-test@example.com";

    private readonly string _dbFile = Path.Combine(
        Path.GetTempPath(), $"crm-golive-{Guid.NewGuid():N}.db");

    /// <summary>
    /// A host pointed at a database file we control across several boots, so one test can seed demo
    /// data, shut down, and boot again with the reset armed — which is how it happens in production.
    /// </summary>
    private class Host : WebApplicationFactory<Program>
    {
        private readonly string _dbFile;
        private readonly Dictionary<string, string?> _extra;

        public Host(string dbFile, Dictionary<string, string?>? extra = null)
        {
            _dbFile = dbFile;
            _extra = extra ?? new();
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Host configuration: under minimal hosting this is the layer that is in place before
            // the API's services read it. The suite stays on the Development environment so that
            // appsettings.Production.json cannot redirect it at the real database path, and reaches
            // the production-only code path through GoLive:AllowNonProduction instead.
            builder.ConfigureHostConfiguration(config =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_dbFile}",
                    ["Database:Provider"] = "Sqlite",
                    ["Jwt:Issuer"] = "CRM",
                    ["Jwt:Audience"] = "CRM",
                    ["Jwt:Secret"] = "test-secret-must-be-at-least-32-characters-long-for-hmac-sha256-tests",
                    ["Cors:Origins:0"] = "http://localhost",
                    ["Integrations:Email:Provider"] = "Stub",
                    ["Integrations:Sms:Provider"] = "Stub",
                    ["Integrations:Lyons:Provider"] = "Stub",
                    ["Ai:Provider"] = "Stub",
                    ["Seed:DummyData"] = "false",
                    // Cleared so a value in appsettings.json can never arm a test run by accident.
                    ["GoLive:Reset"] = "false",
                    ["GoLive:ConfirmPhrase"] = "",
                    ["GoLive:SuperAdminEmail"] = "",
                };
                foreach (var (k, v) in _extra) settings[k] = v;
                config.AddInMemoryCollection(settings);
            });
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }

    /// <summary>Boots a host, runs an assertion against its database, and shuts it down again.</summary>
    private async Task WithHostAsync(Dictionary<string, string?>? extra, Func<AppDbContext, UserManager<ApplicationUser>, Task> act)
    {
        await using var host = new Host(_dbFile, extra);
        _ = host.CreateClient();   // forces startup, which is where seeding happens
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await act(db, users);
    }

    private static Dictionary<string, string?> Armed(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["GoLive:Reset"] = "true",
            ["GoLive:ConfirmPhrase"] = GoLiveReset.RequiredConfirmPhrase,
            ["GoLive:SuperAdminEmail"] = OwnerEmail,
            ["GoLive:SuperAdminName"] = "Owner Under Test",
            ["GoLive:AllowNonProduction"] = "true",
        };
        foreach (var (k, v) in overrides) settings[k] = v;
        return settings;
    }

    // ── Refusals ────────────────────────────────────────────────────────────

    /// <summary>The state every ordinary deploy is in. Nothing may be touched.</summary>
    [Fact]
    public async Task Does_nothing_when_not_armed()
    {
        await WithHostAsync(new() { ["Seed:DummyData"] = "true" }, async (db, users) =>
        {
            Assert.False(await db.PlatformStates.IgnoreQueryFilters().AnyAsync(x => x.Key == GoLiveReset.MarkerKey));
            // The demo installation is intact: the built-in accounts are still there.
            Assert.NotNull(await users.FindByNameAsync("superadmin"));
            Assert.True(await db.Agencies.IgnoreQueryFilters().AnyAsync());
        });
    }

    /// <summary>A flag on its own is not enough — config files get copied between environments.</summary>
    [Fact]
    public async Task Refuses_without_the_confirm_phrase()
    {
        await WithHostAsync(Armed(("GoLive:ConfirmPhrase", "not-the-phrase")), async (db, users) =>
        {
            Assert.False(await db.PlatformStates.IgnoreQueryFilters().AnyAsync(x => x.Key == GoLiveReset.MarkerKey));
            Assert.NotNull(await users.FindByNameAsync("superadmin"));
            Assert.Null(await users.FindByEmailAsync(OwnerEmail));
        });
    }

    /// <summary>Wiping the database with no owner address would lock everyone out permanently.</summary>
    [Fact]
    public async Task Refuses_without_an_owner_email()
    {
        await WithHostAsync(Armed(("GoLive:SuperAdminEmail", "")), async (db, users) =>
        {
            Assert.False(await db.PlatformStates.IgnoreQueryFilters().AnyAsync(x => x.Key == GoLiveReset.MarkerKey));
            Assert.NotNull(await users.FindByNameAsync("superadmin"));
        });
    }

    [Fact]
    public async Task Refuses_when_the_owner_email_is_not_an_address()
    {
        await WithHostAsync(Armed(("GoLive:SuperAdminEmail", "muhammadakash")), async (db, users) =>
        {
            Assert.False(await db.PlatformStates.IgnoreQueryFilters().AnyAsync(x => x.Key == GoLiveReset.MarkerKey));
            Assert.NotNull(await users.FindByNameAsync("superadmin"));
        });
    }

    /// <summary>
    /// The switch lives in a config file every environment reads, so the environment check is the
    /// only thing standing between arming the changeover and emptying a developer's database.
    /// </summary>
    [Fact]
    public async Task Refuses_outside_production()
    {
        await using var host = new DevelopmentHost(_dbFile);
        _ = host.CreateClient();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.False(await db.PlatformStates.IgnoreQueryFilters().AnyAsync(x => x.Key == GoLiveReset.MarkerKey));
        Assert.NotNull(await users.FindByNameAsync("superadmin"));
        Assert.Null(await users.FindByEmailAsync(OwnerEmail));
    }

    /// <summary>Fully armed, but without the test-only escape — so the environment check decides.</summary>
    private sealed class DevelopmentHost : Host
    {
        public DevelopmentHost(string dbFile)
            : base(dbFile, Armed(("GoLive:AllowNonProduction", "false"))) { }
    }

    // ── The reset itself ────────────────────────────────────────────────────

    /// <summary>
    /// The whole changeover, in the order it happens on the box: a demo installation with data,
    /// then a deploy with the reset armed, then an ordinary deploy afterwards.
    /// </summary>
    [Fact]
    public async Task Wipes_the_demo_data_and_leaves_exactly_one_invited_superadmin()
    {
        // 1. A demo installation, with seeded test data in it.
        var demoUsers = 0;
        await WithHostAsync(new() { ["Seed:DummyData"] = "true" }, async (db, users) =>
        {
            demoUsers = await users.Users.CountAsync();
            Assert.True(demoUsers > 1, "the demo seed should create more than one user");
            Assert.True(await db.Leads.IgnoreQueryFilters().AnyAsync(), "the demo seed should create leads");
        });

        // 2. The go-live deploy.
        await WithHostAsync(Armed(), async (db, users) =>
        {
            // Exactly one account on the whole installation.
            var remaining = await users.Users.ToListAsync();
            Assert.Single(remaining);

            var owner = remaining[0];
            Assert.Equal(OwnerEmail, owner.Email);
            Assert.Equal(Guid.Empty, owner.AgencyId);          // cross-tenant, by convention
            Assert.True(owner.IsActive);
            Assert.True(owner.MustChangePassword);             // the temp password is single-use
            Assert.NotNull(owner.InvitationSentAt);
            Assert.Contains(Roles.SuperAdmin, await users.GetRolesAsync(owner));

            // The built-in demo accounts are gone and must not come back.
            Assert.Null(await users.FindByNameAsync("admin"));
            Assert.Null(await users.FindByNameAsync("superadmin"));

            // Every trace of the test data is gone.
            Assert.False(await db.Leads.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.Sales.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.Agencies.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.CallCenters.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.Notifications.IgnoreQueryFilters().AnyAsync());

            // Reference data the platform cannot run without was rebuilt.
            Assert.True(await db.Permissions.AnyAsync(), "permissions should be re-seeded");
            Assert.True(await db.AppModules.AnyAsync(), "modules should be re-seeded");
            Assert.True(await db.AcademyCourses.AnyAsync(), "the Academy curriculum should be re-seeded");

            var marker = await db.PlatformStates.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Key == GoLiveReset.MarkerKey);
            Assert.NotNull(marker);

            // A snapshot of what was deleted must exist, and must be a real file — the reset is
            // supposed to abandon itself rather than destroy anything it could not preserve first.
            var backups = Directory.GetFiles(Path.GetDirectoryName(_dbFile)!, "pre-go-live-*.db");
            Assert.NotEmpty(backups);
            Assert.All(backups, b => Assert.True(new FileInfo(b).Length > 0, "the backup should not be empty"));
            Assert.Contains("Backup:", marker!.Detail);
        });

        // 3. An ordinary deploy afterwards — the marker must hold, even with the flag still set.
        await WithHostAsync(Armed(), async (db, users) =>
        {
            Assert.Single(await users.Users.ToListAsync());
            Assert.Single(await db.PlatformStates.IgnoreQueryFilters()
                .Where(x => x.Key == GoLiveReset.MarkerKey).ToListAsync());
        });
    }

    /// <summary>
    /// Once live, the built-in demo accounts must never be recreated — default credentials on an
    /// internet-facing platform are how installations get taken over.
    /// </summary>
    [Fact]
    public async Task Never_recreates_the_default_accounts_once_live()
    {
        await WithHostAsync(Armed(), (_, _) => Task.CompletedTask);

        // A later deploy, with the go-live config removed entirely, as it will be.
        await WithHostAsync(new() { ["Seed:DummyData"] = "true" }, async (db, users) =>
        {
            Assert.Null(await users.FindByNameAsync("admin"));
            Assert.Null(await users.FindByNameAsync("superadmin"));
            Assert.Single(await users.Users.ToListAsync());

            // …and the demo seeder was refused despite being switched on.
            Assert.False(await db.Leads.IgnoreQueryFilters().AnyAsync());
            Assert.False(await db.Agencies.IgnoreQueryFilters().AnyAsync());
        });
    }

    /// <summary>The owner is asked for two-factor enrolment, not offered it.</summary>
    [Fact]
    public void Two_factor_is_mandatory_for_superadmin()
    {
        Assert.Contains(Roles.SuperAdmin, Roles.RequireTwoFactor);
        Assert.True(Roles.TwoFactorMandatory(new[] { Roles.SuperAdmin }));
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_dbFile)!;
        var leftovers = Directory.EnumerateFiles(directory, Path.GetFileNameWithoutExtension(_dbFile) + "*")
            .Concat(Directory.EnumerateFiles(directory, "pre-go-live-*.db"));
        foreach (var path in leftovers)
        {
            try { File.Delete(path); } catch { /* a leftover temp file is not worth failing a test over */ }
        }
    }
}
