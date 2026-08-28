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

    /// <summary>
    /// Keep EVERY snapshot from this window, so a recent mistake can be undone at 6-hourly
    /// granularity. Default 48h.
    /// </summary>
    private TimeSpan RecentWindow =>
        TimeSpan.FromHours(Math.Clamp(_config.GetValue("Backups:RecentHours", 48), 6, 720));

    /// <summary>
    /// Beyond that window keep ONE snapshot per calendar day, for this many days. Default 30 —
    /// corruption is often noticed weeks later, and a 3-day history would already be gone.
    /// </summary>
    private int DailyDays => Math.Clamp(_config.GetValue("Backups:DailyDays", 30), 1, 3650);

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

    /// <summary>
    /// Tiered retention, so recovery is possible from both "an hour ago" and "three weeks ago":
    ///   • every snapshot inside <see cref="RecentWindow"/> (fine-grained, recent mistakes)
    ///   • then the newest ONE PER DAY for <see cref="DailyDays"/> days (long look-back)
    ///   • everything else is deleted.
    ///
    /// Only files this service created (crm-yyyyMMdd-HHmmss.db) are ever considered. Hand-made
    /// safety copies such as crm-precallcenter-*.db are deliberately left alone — they were taken
    /// before risky migrations and must not vanish because they aged out of a rolling window.
    /// </summary>
    private void Prune(string dir)
    {
        try
        {
            var mine = new DirectoryInfo(dir).GetFiles("crm-*.db")
                .Select(f => (File: f, Taken: TakenAt(f.Name)))
                .Where(x => x.Taken is not null)          // skips the hand-made copies
                .Select(x => (x.File, Taken: x.Taken!.Value))
                .OrderByDescending(x => x.Taken)
                .ToList();

            var now = DateTime.UtcNow;
            var cutoff = now.Date.AddDays(-DailyDays);
            var keep = new HashSet<string>(StringComparer.Ordinal);

            foreach (var x in mine)
                if (now - x.Taken <= RecentWindow) keep.Add(x.File.FullName);

            foreach (var day in mine.GroupBy(x => x.Taken.Date))
                if (day.Key >= cutoff)
                    keep.Add(day.OrderByDescending(x => x.Taken).First().File.FullName);

            var stale = mine.Where(x => !keep.Contains(x.File.FullName)).ToList();
            foreach (var x in stale)
            {
                try { x.File.Delete(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not prune old backup {Name}", x.File.Name); }
            }

            if (stale.Count > 0)
                _logger.LogInformation(
                    "Pruned {Count} snapshot(s); kept {Kept} (all within {Hours}h, then one per day for {Days} days).",
                    stale.Count, keep.Count, (int)RecentWindow.TotalHours, DailyDays);
        }
        catch (Exception ex)
        {
            // Housekeeping only — a prune failure must never fail the backup that just succeeded.
            _logger.LogWarning(ex, "Backup pruning failed.");
        }
    }

    /// <summary>
    /// The timestamp encoded in a snapshot this service wrote, or null for any other file. Parsing
    /// the NAME rather than trusting the filesystem keeps pruning correct even if a copy or restore
    /// rewrites creation times.
    /// </summary>
    public static DateTime? TakenAt(string fileName)
    {
        // crm-yyyyMMdd-HHmmss.db
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"^crm-(\d{8}-\d{6})\.db$");
        if (!m.Success) return null;
        return DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt) ? dt : null;
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
