using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.Infrastructure.Persistence.Seed;

/// <summary>
/// The one-time changeover from a demo installation to a live platform: every row of test data is
/// removed, and the installation is left with exactly one account — the owner's SuperAdmin, invited
/// by email and required to enrol in two-factor authentication before they can use anything.
///
/// This destroys data, so it is deliberately hard to trigger by accident. FIVE conditions must all
/// hold, and any one of them missing makes this a no-op:
///
///   0. The host environment is Production. The switch has to live in <c>appsettings.json</c>, because
///      that is the only config file a deploy can reach — the box keeps its own
///      <c>appsettings.Production.json</c>. That file is read by every environment, so without this
///      check, arming the changeover would also empty every developer's database and every test
///      host's. The environment is what separates "the live platform" from "a copy of its config".
///   1. <c>GoLive:Reset</c> is true.
///   2. <c>GoLive:ConfirmPhrase</c> matches <see cref="RequiredConfirmPhrase"/> exactly — a flag
///      alone is too easy to set by copying a config file around.
///   3. <c>GoLive:SuperAdminEmail</c> is a real address, since the account it creates is the only
///      way back in afterwards.
///   4. No <c>go-live-reset</c> marker exists in <see cref="PlatformState"/>. This is the condition
///      that actually protects live customer data: flags stay switched on and services restart, so
///      the config can never be the thing standing between a redeploy and a second wipe.
///   5. A snapshot of the database was taken successfully. This is a precondition rather than a
///      courtesy: if the data cannot be preserved first, the reset is abandoned and the platform
///      starts up untouched.
/// </summary>
public static class GoLiveReset
{
    /// <summary>
    /// Must match <c>GoLive:ConfirmPhrase</c>. Spelled out in full so that finding this constant in
    /// the code tells you exactly what it does.
    /// </summary>
    public const string RequiredConfirmPhrase = "DELETE-ALL-DATA-AND-GO-LIVE";

    /// <summary>The marker key written once the reset has run.</summary>
    public const string MarkerKey = "go-live-reset";

    /// <summary>
    /// Tables the wipe must not touch. Everything else is emptied — including the ASP.NET Identity
    /// tables — and then rebuilt by the ordinary seeders, which are all idempotent.
    ///
    /// An allow-list is the wrong shape here. There are 85-plus entity sets and more arrive every
    /// week; anything forgotten off a delete-list survives as invisible test data, and the one place
    /// that must not happen is a platform going live. So the rule is inverted: everything goes unless
    /// it is named here.
    /// </summary>
    private static readonly HashSet<string> PreservedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "__EFMigrationsHistory",   // deleting this would make EF try to re-apply every migration
        "sqlite_sequence",         // SQLite's own bookkeeping, not ours
    };

    /// <summary>
    /// Runs the reset if armed. Returns true when it actually did something, so the caller knows the
    /// database it is about to seed is empty.
    /// </summary>
    public static async Task<bool> RunIfArmedAsync(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        IConfiguration config,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        if (!config.GetValue("GoLive:Reset", false)) return false;

        // AllowNonProduction exists so the tests below this class can exercise the real path. It is
        // never set in any shipped config, and on its own it does nothing — the phrase, the address
        // and the marker still all have to line up.
        if (!environment.IsProduction() && !config.GetValue("GoLive:AllowNonProduction", false))
        {
            logger.LogInformation(
                "GoLive:Reset is set but this is the {Environment} environment, so nothing was touched. " +
                "The changeover only runs on the live platform.",
                environment.EnvironmentName);
            return false;
        }

        var phrase = config["GoLive:ConfirmPhrase"];
        if (!string.Equals(phrase, RequiredConfirmPhrase, StringComparison.Ordinal))
        {
            // Loud, because someone has half-armed a destructive operation and needs to know it did
            // not run rather than assuming it did.
            logger.LogError(
                "GoLive:Reset is set but GoLive:ConfirmPhrase does not match. No data was touched. " +
                "Set GoLive:ConfirmPhrase to the exact phrase in GoLiveReset.RequiredConfirmPhrase to proceed.");
            return false;
        }

        var email = config["GoLive:SuperAdminEmail"]?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            logger.LogError(
                "GoLive:Reset is armed but GoLive:SuperAdminEmail is missing or not an email address. " +
                "No data was touched — wiping the database without a way back in would lock everyone out.");
            return false;
        }

        // The marker is checked last so that a misconfiguration is reported even after the reset has
        // already happened; otherwise a half-armed config would stay silent forever.
        var alreadyRan = await db.PlatformStates.IgnoreQueryFilters()
            .AnyAsync(x => x.Key == MarkerKey, ct);
        if (alreadyRan)
        {
            logger.LogInformation(
                "Go-live reset already ran on this database; skipping. Remove GoLive:Reset from config to silence this.");
            return false;
        }

        logger.LogWarning("Go-live reset is armed. Backing the database up, then deleting all data.");

        // The backup is a precondition, not a courtesy. If there is data to lose and it cannot be
        // copied, the reset is abandoned and the platform starts normally with everything intact:
        // failing to go live is an afternoon's inconvenience, wiping without a backup is permanent.
        var (backedUp, backup) = await TryBackupDatabaseAsync(db, logger, ct);
        if (!backedUp)
        {
            logger.LogError(
                "Go-live reset ABANDONED: the database could not be backed up first. Nothing was deleted. " +
                "Check free disk space and the permissions on the database directory, then deploy again.");
            return false;
        }

        var deleted = await WipeAllTablesAsync(db, logger, ct);

        // Roles first: creating the owner's account needs SuperAdmin to exist, and the wipe removed it.
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));

        db.PlatformStates.Add(new PlatformState
        {
            Key = MarkerKey,
            AppliedAt = DateTime.UtcNow,
            Detail = $"Emptied {deleted} tables and provisioned {email} as the sole SuperAdmin. " +
                     (backup is null ? "No file backup was taken." : $"Backup: {backup}"),
        });
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Go-live reset complete: {Tables} tables emptied. Backup: {Backup}",
            deleted, backup ?? "none");

        return true;
    }

    /// <summary>
    /// Snapshots the database beside itself before the wipe.
    ///
    /// Uses <c>VACUUM INTO</c> rather than copying the file. In WAL mode the newest committed
    /// transactions live in a separate <c>-wal</c> file, so a plain copy of the <c>.db</c> can be
    /// missing the most recent writes — exactly the data someone restoring a backup would come
    /// looking for. VACUUM INTO writes one consistent, fully-checkpointed file.
    /// </summary>
    /// <returns>
    /// Whether it is safe to proceed, and the backup path. Safe-to-proceed is true with a null path
    /// only when there was no database file to begin with — a fresh installation has nothing to lose.
    /// </returns>
    private static async Task<(bool Safe, string? Path)> TryBackupDatabaseAsync(
        AppDbContext db, ILogger logger, CancellationToken ct)
    {
        string? target = null;
        try
        {
            var connection = db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connection))
            {
                logger.LogError("No connection string, so no backup could be taken.");
                return (false, null);
            }

            var source = new SqliteConnectionStringBuilder(connection).DataSource;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                // Nothing on disk yet: a brand-new installation being brought up live directly.
                logger.LogInformation("No existing database file, so there is nothing to back up.");
                return (true, null);
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".";
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            target = Path.Combine(directory, $"pre-go-live-{stamp}.db");

            // VACUUM INTO refuses to overwrite, which is the behaviour we want from a backup path.
            await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{target.Replace("'", "''")}'", ct);

            var size = new FileInfo(target).Length;
            if (size == 0)
            {
                logger.LogError("The backup at {Path} came out empty; treating that as a failure.", target);
                return (false, null);
            }

            logger.LogWarning("Pre-go-live backup written to {Path} ({Bytes} bytes)", target, size);
            return (true, target);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not back the database up before the go-live reset.");
            // Don't leave a partial file that looks like a usable backup.
            try { if (target is not null && File.Exists(target)) File.Delete(target); } catch { }
            return (false, null);
        }
    }

    /// <summary>
    /// Empties every table except <see cref="PreservedTables"/>.
    ///
    /// Foreign keys are switched off for the duration rather than working out a safe delete order.
    /// The whole schema is being emptied, so there is no ordering that leaves a valid graph part-way
    /// through, and every table ends up empty regardless of the order they are visited in.
    /// </summary>
    private static async Task<int> WipeAllTablesAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var tables = new List<string>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                tables.Add(reader.GetString(0));
        }

        var targets = tables.Where(t => !PreservedTables.Contains(t)).ToList();

        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", ct);
        try
        {
            foreach (var table in targets)
            {
                // Table names come from sqlite_master, not from user input, so they cannot be hostile.
                // Quoted anyway, since several of ours would otherwise need escaping.
                await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\"", ct);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON", ct);
        }

        logger.LogWarning("Emptied {Count} tables: {Tables}", targets.Count, string.Join(", ", targets));
        return targets.Count;
    }
}
