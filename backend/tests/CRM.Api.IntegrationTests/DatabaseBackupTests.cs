using CRM.Infrastructure.BackgroundJobs;
using CRM.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// Proves the backup actually produces a RESTORABLE database, not merely a file. The product's
/// entire dataset lives in one SQLite file on one box, so "a backup exists" is worth nothing unless
/// it opens and contains the data.
/// </summary>
public class DatabaseBackupTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public DatabaseBackupTests(CrmWebAppFactory factory) => _factory = factory;

    private DatabaseBackupService BuildService(string backupDir)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backups:Enabled"] = "true",
                ["Backups:Directory"] = backupDir,
                ["Backups:RecentHours"] = "48",
                ["Backups:DailyDays"] = "30",
            })
            .Build();

        return new DatabaseBackupService(
            _factory.Services, config, NullLogger<DatabaseBackupService>.Instance);
    }

    [Fact]
    public async Task Backup_writes_a_snapshot_that_opens_and_contains_the_data()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crm-backup-{Guid.NewGuid():N}");
        var service = BuildService(dir);

        var path = await service.RunOnceAsync(CancellationToken.None);

        Assert.NotNull(path);
        Assert.True(File.Exists(path), "the backup file should exist on disk");
        Assert.True(new FileInfo(path!).Length > 0, "the backup should not be empty");

        // The real assertion: the snapshot is a valid SQLite database we can query. A file-copy
        // backup of a live database can be a torn page — VACUUM INTO must not be.
        await using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";
        var tables = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.True(tables > 0, "the restored snapshot should contain the schema");

        // And it must carry real rows, not just an empty schema — the seeder always creates agencies.
        cmd.CommandText = "SELECT COUNT(*) FROM Agencies;";
        var agencies = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.True(agencies > 0, "the restored snapshot should contain seeded data");

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Retention_keeps_recent_snapshots_one_per_day_and_drops_the_rest()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crm-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var now = DateTime.UtcNow;
        string Name(DateTime t) => $"crm-{t:yyyyMMdd-HHmmss}.db";
        async Task Seed(DateTime t) => await File.WriteAllTextAsync(Path.Combine(dir, Name(t)), "x");

        // Inside the 48h window: every one of these must survive (fine-grained recent recovery).
        var recent = new[] { now.AddHours(-2), now.AddHours(-8), now.AddHours(-20), now.AddHours(-40) };
        foreach (var t in recent) await Seed(t);

        // 10 days ago, three on the SAME day: only the newest of them should survive.
        var oldDay = now.AddDays(-10).Date.AddHours(9);
        await Seed(oldDay);
        await Seed(oldDay.AddHours(5));
        var newestOnOldDay = oldDay.AddHours(11);
        await Seed(newestOnOldDay);

        // Older than the 30-day daily window: must be dropped.
        var ancient = now.AddDays(-100).Date.AddHours(3);
        await Seed(ancient);

        // A hand-made pre-migration copy — must NEVER be pruned, however old.
        var manual = Path.Combine(dir, "crm-precallcenter-20260723-223612.db");
        await File.WriteAllTextAsync(manual, "manual");

        // Keep = irrelevant now; the service is configured by RecentHours / DailyDays.
        var service = BuildService(dir);
        await service.RunOnceAsync(CancellationToken.None);

        var left = Directory.GetFiles(dir).Select(Path.GetFileName).ToHashSet()!;

        foreach (var t in recent)
            Assert.Contains(Name(t), left);                       // all recent kept

        Assert.Contains(Name(newestOnOldDay), left);              // newest of that day kept
        Assert.DoesNotContain(Name(oldDay), left);                // its earlier siblings pruned
        Assert.DoesNotContain(Name(oldDay.AddHours(5)), left);

        Assert.DoesNotContain(Name(ancient), left);               // beyond the daily window
        Assert.Contains("crm-precallcenter-20260723-223612.db", left);  // manual copy untouched

        Directory.Delete(dir, recursive: true);
    }

    [Theory]
    [InlineData("crm-20260828-174140.db", true)]
    [InlineData("crm-precallcenter-20260723-223612.db", false)]  // hand-made — never ours to prune
    [InlineData("crm-preintake-20260724-041042.db", false)]
    [InlineData("something-else.db", false)]
    [InlineData("crm-20261332-999999.db", false)]                // well-formed but not a real date
    public void Only_snapshots_this_service_wrote_are_prunable(string fileName, bool isOurs)
        => Assert.Equal(isOurs, CRM.Infrastructure.BackgroundJobs.DatabaseBackupService.TakenAt(fileName) is not null);
}
