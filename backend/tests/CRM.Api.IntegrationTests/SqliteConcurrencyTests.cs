using Microsoft.Data.Sqlite;
using Xunit;

namespace CRM.Api.IntegrationTests;

/// <summary>
/// The database settings that decide whether the product is responsive under load.
///
/// SQLite's default journal mode makes a writer lock the whole file against every reader. On a floor
/// of forty agents that serialises the entire CRM behind whoever is saving, which is what "very
/// slow" meant. These tests pin the behaviour down against a real file database, because the mode
/// only matters for a file — an in-memory database reports "memory" and proves nothing.
/// </summary>
public class SqliteConcurrencyTests
{
    /// <summary>The pragmas the application applies on every connection. Mirrors SqlitePragmaInterceptor.</summary>
    private const string Pragmas =
        "PRAGMA journal_mode=WAL;PRAGMA busy_timeout=10000;PRAGMA synchronous=NORMAL;PRAGMA foreign_keys=ON;";

    private static SqliteConnection OpenTuned(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static string Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    [Fact]
    public void A_tuned_connection_runs_in_wal_mode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wal-{Guid.NewGuid():N}.db");
        try
        {
            using var conn = OpenTuned(path);
            Assert.Equal("wal", Scalar(conn, "PRAGMA journal_mode;").ToLowerInvariant());
            Assert.Equal("1", Scalar(conn, "PRAGMA foreign_keys;"));
            Assert.Equal("10000", Scalar(conn, "PRAGMA busy_timeout;"));
        }
        finally { TryDelete(path); }
    }

    /// <summary>
    /// THE property worth having. Under the default journal an open write transaction blocks every
    /// reader; under WAL a reader sails straight past it. This is the difference agents feel.
    /// </summary>
    [Fact]
    public void A_reader_is_not_blocked_by_an_open_write()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wal-{Guid.NewGuid():N}.db");
        try
        {
            using var setup = OpenTuned(path);
            using (var create = setup.CreateCommand())
            {
                create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT); INSERT INTO t (v) VALUES ('before');";
                create.ExecuteNonQuery();
            }

            // A writer holds an open transaction, exactly as a slow save would.
            using var writer = OpenTuned(path);
            using var tx = writer.BeginTransaction();
            using (var write = writer.CreateCommand())
            {
                write.Transaction = tx;
                write.CommandText = "INSERT INTO t (v) VALUES ('during');";
                write.ExecuteNonQuery();
            }

            // …and a second connection reads while that write is still uncommitted.
            using var reader = OpenTuned(path);
            Assert.Equal("1", Scalar(reader, "SELECT COUNT(*) FROM t;"));   // sees the committed state only

            tx.Commit();
            Assert.Equal("2", Scalar(reader, "SELECT COUNT(*) FROM t;"));
        }
        finally { TryDelete(path); }
    }

    /// <summary>
    /// The control: the same sequence on SQLite's default settings is where the contention lives.
    /// Kept so the reason for the pragmas above stays legible to whoever reads this next.
    /// </summary>
    [Fact]
    public void The_default_journal_mode_is_not_wal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nowal-{Guid.NewGuid():N}.db");
        try
        {
            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            Assert.NotEqual("wal", Scalar(conn, "PRAGMA journal_mode;").ToLowerInvariant());
        }
        finally { TryDelete(path); }
    }

    private static void TryDelete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* temp file; not worth failing a test over */ }
        }
    }
}
