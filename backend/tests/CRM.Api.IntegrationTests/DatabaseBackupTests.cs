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
                ["Backups:Keep"] = "3",
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
    public async Task Backup_prunes_old_snapshots_and_keeps_the_newest()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crm-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        // Five stale snapshots, deliberately older than anything the run will create.
        for (var i = 0; i < 5; i++)
        {
            var stale = Path.Combine(dir, $"crm-2000010{i}-000000.db");
            await File.WriteAllTextAsync(stale, "old");
            File.SetCreationTimeUtc(stale, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
        }

        var service = BuildService(dir);          // Keep = 3
        await service.RunOnceAsync(CancellationToken.None);

        var remaining = Directory.GetFiles(dir, "crm-*.db");
        Assert.Equal(3, remaining.Length);

        // The one just written is the newest, so it must have survived the prune.
        Assert.Contains(remaining, f => !Path.GetFileName(f).StartsWith("crm-2000", StringComparison.Ordinal));

        Directory.Delete(dir, recursive: true);
    }
}
