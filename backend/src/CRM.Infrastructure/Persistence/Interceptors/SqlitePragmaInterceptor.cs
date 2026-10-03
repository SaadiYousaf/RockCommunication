using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace CRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Applies the SQLite settings this application actually needs, on every connection it opens.
///
/// WHY: SQLite's default journal mode is <c>DELETE</c>, under which a writer takes an exclusive lock
/// on the WHOLE database — every reader waits behind every write. On a floor where forty agents are
/// dialling, chatting, saving dispositions and polling at the same time, that serialises the entire
/// product behind whoever happens to be writing. And with no busy timeout, a connection that finds
/// the database locked does not wait its turn: it fails immediately with SQLITE_BUSY.
///
/// WAL (write-ahead logging) is the fix. Readers and the writer stop blocking each other, so reads
/// — which are almost everything the CRM does — keep flowing while a write is in progress.
///
/// Set here rather than in the connection string because Microsoft.Data.Sqlite exposes only some of
/// these as keywords, and because a pooled connection has to be configured each time it is handed
/// out, not once at startup.
/// </summary>
public class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<SqlitePragmaInterceptor>? _logger;

    public SqlitePragmaInterceptor(ILogger<SqlitePragmaInterceptor>? logger = null) => _logger = logger;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, ct);
    }

    private void Apply(DbConnection connection)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                // Persistent once set, but harmless to re-assert — and an in-memory database simply
                // reports back "memory", which is why the result is not checked.
                "PRAGMA journal_mode=WAL;" +
                // Wait up to 10s for a lock instead of failing instantly. Under WAL this is only
                // reached when two writers genuinely collide; before it, a brief contention spike
                // surfaced to the user as an error.
                "PRAGMA busy_timeout=10000;" +
                // Safe companion to WAL: the OS still flushes at checkpoints, so a crash can cost
                // the last transaction but never corrupts the file. FULL fsyncs on every commit,
                // which on a single disk is the slowest thing SQLite does.
                "PRAGMA synchronous=NORMAL;" +
                // Off by default in SQLite. The schema declares foreign keys; without this they are
                // decoration.
                "PRAGMA foreign_keys=ON;" +
                // Lets the page cache hold a working set rather than re-reading from disk. Negative
                // means KiB, so this is 64 MB.
                "PRAGMA cache_size=-64000;";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // A connection that cannot be tuned still works, just slower. Failing here would take
            // the whole application down over a performance setting.
            _logger?.LogWarning(ex, "Could not apply SQLite pragmas to a new connection.");
        }
    }
}
