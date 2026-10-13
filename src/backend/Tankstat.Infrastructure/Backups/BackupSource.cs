using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// One consistent read of the whole database for a dump: SQLite is copied with its backup API (safe on a live database, unlike copying the
/// file) and the copy is read; the server databases are read inside one transaction at an isolation level that gives one snapshot
/// (PostgreSQL and MySQL <c>REPEATABLE READ</c>; SQL Server <c>SNAPSHOT</c> where the database allows it, else <c>SERIALIZABLE</c>, which
/// makes writers wait while the dump runs).
/// </summary>
internal sealed class BackupSource : IAsyncDisposable
{
    private readonly Func<ValueTask> _cleanUp;

    private BackupSource(DbConnection connection, DbTransaction? transaction, Func<ValueTask> cleanUp)
    {
        Connection = connection;
        Transaction = transaction;
        _cleanUp = cleanUp;
    }

    public DbConnection Connection { get; }
    public DbTransaction? Transaction { get; }

    public DbCommand Command(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = Transaction;
        return command;
    }

    public static async Task<BackupSource> OpenAsync(AppDbContext db, DatabaseProvider provider, ILogger logger, CancellationToken ct)
    {
        if (provider == DatabaseProvider.Sqlite) return await CopySqliteAsync(db, ct);

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        var level = provider == DatabaseProvider.SqlServer ? await SqlServerLevelAsync(connection, logger, ct) : IsolationLevel.RepeatableRead;
        var transaction = await db.Database.BeginTransactionAsync(level, ct);
        return new BackupSource(connection, transaction.GetDbTransaction(), async () =>
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None); // nothing was written; this frees the snapshot
            }
            catch (Exception e) when (e is DbException or InvalidOperationException)
            {
                // The connection broke after or while the tables were read (a restart, an idle-in-transaction timeout), and the transaction
                // went with it. What was read is whole, and an error of the read itself is already on its way out: this one replaces neither.
                logger.LogWarning(e, "The backup's read transaction could not be rolled back; its connection is closed instead");
            }
            finally
            {
                await transaction.DisposeAsync();
                await db.Database.CloseConnectionAsync();
            }
        });
    }

    /// <summary>
    /// <c>SNAPSHOT</c> where the database allows it (<c>ALLOW_SNAPSHOT_ISOLATION ON</c>, which is not the default), else <c>SERIALIZABLE</c>, which
    /// makes writers wait while the dump runs. A database that does not allow snapshots is only reported at the first statement inside the
    /// transaction, not when it begins, so it is asked first.
    /// </summary>
    private static async Task<IsolationLevel> SqlServerLevelAsync(DbConnection connection, ILogger logger, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == SnapshotIsolationOn) return IsolationLevel.Snapshot;
        logger.LogInformation("Snapshot isolation is not allowed on the database (ALLOW_SNAPSHOT_ISOLATION is off); the backup reads under SERIALIZABLE and writers wait meanwhile");
        return IsolationLevel.Serializable;
    }

    /// <summary><c>sys.databases.snapshot_isolation_state</c>: 1 is ON (0 off, 2 and 3 on their way).</summary>
    private const int SnapshotIsolationOn = 1;

    /// <summary>
    /// The copy is a plain database: it is made in a temporary folder of its own (never in the backups folder, which is meant to be safe to
    /// copy anywhere, and would keep it after a crash), and removed with the folder once the dump is done.
    /// </summary>
    private static async Task<BackupSource> CopySqliteAsync(AppDbContext db, CancellationToken ct)
    {
        // The live database is opened as it is and never created: run from another working directory than the server's (the default Data
        // Source is relative), the command fails instead of leaving an empty database there.
        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false }; // opened once, closed with the copy made
        if (source.Mode == SqliteOpenMode.ReadWriteCreate) source.Mode = SqliteOpenMode.ReadWrite;
        var scratch = Directory.CreateTempSubdirectory("tankstat-backup-");
        var copy = new SqliteConnection($"Data Source={Path.Combine(scratch.FullName, "copy.db")};Pooling=False");
        try
        {
            await using (var live = new SqliteConnection(source.ToString()))
            {
                await live.OpenAsync(ct);
                await copy.OpenAsync(ct);
                live.BackupDatabase(copy);
            }
            return new BackupSource(copy, null, async () =>
            {
                await copy.DisposeAsync();
                scratch.Delete(recursive: true);
            });
        }
        catch
        {
            await copy.DisposeAsync();
            scratch.Delete(recursive: true);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _cleanUp();
}
