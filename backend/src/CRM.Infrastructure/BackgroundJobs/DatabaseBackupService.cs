using CRM.Domain.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CRM.Infrastructure.Persistence;

namespace CRM.Infrastructure.BackgroundJobs;

/// <summary>
/// Takes periodic snapshots of the SQLite database and prunes old ones.
///
/// WHY THIS EXISTS: the whole product — customers, sales, commission, payroll, encrypted bank and
/// identity data — lives in one SQLite file on one small box. There was no backup of any kind, so a
/// lost or corrupted instance meant total, unrecoverable data loss. That box has already gone down
/// once.
///
/// HOW: <c>VACUUM INTO</c> is SQLite's supported way to snapshot a LIVE database. It takes a read
/// lock, writes a fully-consistent compacted copy, and (unlike copying the file) can never capture a
/// half-written page or miss the WAL. Writers are only blocked for the duration of the copy.
///
/// WHERE: <c>Backups:Directory</c>, defaulting to a sibling of the database file. Deliberately NOT
/// under the app directory, because the deploy rsyncs over that. Configure it onto a different
/// volume (or sync it off-box) for real durability — a backup on the same disk survives a bad
/// migration or a botched deploy, but not the disk itself.
///
/// Every failure is caught and logged: a backup problem must never take the API down.
/// </summary>
public class DatabaseBackupService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(IServiceProvider sp, IConfiguration config, ILogger<DatabaseBackupService> logger)
    {
        _sp = Guard.AgainstNull(sp);
        _config = Guard.AgainstNull(config);
        _logger = Guard.AgainstNull(logger);
    }

    private TimeSpan Interval =>
        TimeSpan.FromHours(Math.Clamp(_config.GetValue("Backups:IntervalHours", 6), 1, 24));

    /// <summary>How many snapshots to keep. Older ones are pruned oldest-first.</summary>
    private int Keep => Math.Clamp(_config.GetValue("Backups:Keep", 14), 1, 200);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Backups:Enabled", true))
        {
            _logger.LogInformation("Database backups are disabled (Backups:Enabled = false).");
            return;
        }

        // A short initial delay keeps startup light and avoids competing with migrations.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let a backup failure stop the loop — or the app.
                _logger.LogError(ex, "Database backup failed; will retry at the next interval.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Takes one snapshot and prunes old ones. Public so an admin endpoint could reuse it.</summary>
    public async Task<string?> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Only SQLite supports VACUUM INTO; on any other provider this is a no-op rather than a crash.
        if (!(db.Database.ProviderName ?? "").Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Backup skipped — provider {Provider} is not SQLite.", db.Database.ProviderName);
            return null;
        }

        var source = SourcePath(db);
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            _logger.LogWarning("Backup skipped — could not resolve the database file from the connection string.");
            return null;
        }

        var dir = BackupDirectory(source);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(dir, $"crm-{stamp}.db");

        // VACUUM INTO refuses to overwrite, so a stale partial file must go first.
        if (File.Exists(target)) File.Delete(target);

        // Parameters aren't allowed in VACUUM INTO, so the path is inlined — escape quotes to keep
        // it a valid SQL string literal even if the directory ever contains one.
        var literal = target.Replace("'", "''");
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{literal}'", ct);

        var size = new FileInfo(target).Length;
        _logger.LogInformation("Database backup written: {Path} ({SizeKb} KB)", target, size / 1024);

        Prune(dir);
        return target;
    }

    /// <summary>Keeps the newest <see cref="Keep"/> snapshots and deletes the rest.</summary>
    private void Prune(string dir)
    {
        try
        {
            var stale = new DirectoryInfo(dir)
                .GetFiles("crm-*.db")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(Keep)
                .ToList();

            foreach (var f in stale)
            {
                try { f.Delete(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not prune old backup {Name}", f.Name); }
            }
            if (stale.Count > 0)
                _logger.LogInformation("Pruned {Count} old backup(s), keeping the newest {Keep}.", stale.Count, Keep);
        }
        catch (Exception ex)
        {
            // Pruning is housekeeping — a failure here must not fail the backup that just succeeded.
            _logger.LogWarning(ex, "Backup pruning failed.");
        }
    }

    /// <summary>The on-disk path of the live database, read from the active connection string.</summary>
    private static string? SourcePath(AppDbContext db)
    {
        try
        {
            var cs = db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(cs)) return null;
            var path = new SqliteConnectionStringBuilder(cs).DataSource;
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch { return null; }
    }

    private string BackupDirectory(string sourcePath)
    {
        var configured = _config["Backups:Directory"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured!;
        // Default: a "backups" folder beside the database (e.g. /var/lib/crm/backups), which the
        // deploy does not touch.
        var dbDir = Path.GetDirectoryName(sourcePath) ?? ".";
        return Path.Combine(dbDir, "backups");
    }
}
