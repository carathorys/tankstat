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
/// (PostgreSQL and MySQL <c>REPEATABLE READ</c>; SQL Server <c>SNAPSHOT</c>, and <c>SERIALIZABLE</c> where that is not allowed on the
/// database, which makes writers wait while the dump runs).
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

    /// <param name="scratchFolder">Where SQLite's copy is made (next to the backup, so it has the room the backup has).</param>
    public static async Task<BackupSource> OpenAsync(AppDbContext db, DatabaseProvider provider, string scratchFolder, ILogger logger, CancellationToken ct)
    {
        if (provider == DatabaseProvider.Sqlite) return await CopySqliteAsync(db, scratchFolder, ct);

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        IDbContextTransaction transaction;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(provider == DatabaseProvider.SqlServer ? IsolationLevel.Snapshot : IsolationLevel.RepeatableRead, ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException e) when (provider == DatabaseProvider.SqlServer && e.Number == 3952)
        {
            logger.LogInformation("Snapshot isolation is not allowed on the database (ALLOW_SNAPSHOT_ISOLATION is off); the backup reads under SERIALIZABLE and writers wait meanwhile");
            transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        }
        return new BackupSource(connection, transaction.GetDbTransaction(), async () =>
        {
            await transaction.RollbackAsync(CancellationToken.None); // nothing was written; this frees the snapshot
            await transaction.DisposeAsync();
            await db.Database.CloseConnectionAsync();
        });
    }

    private static async Task<BackupSource> CopySqliteAsync(AppDbContext db, string scratchFolder, CancellationToken ct)
    {
        var copyPath = Path.Combine(scratchFolder, $"tankstat-{Guid.NewGuid():N}.db-copy");
        var copy = new SqliteConnection($"Data Source={copyPath};Pooling=False");
        try
        {
            await using (var live = new SqliteConnection(db.Database.GetConnectionString()))
            {
                await live.OpenAsync(ct);
                await copy.OpenAsync(ct);
                live.BackupDatabase(copy);
            }
            return new BackupSource(copy, null, async () =>
            {
                await copy.DisposeAsync();
                File.Delete(copyPath);
            });
        }
        catch
        {
            await copy.DisposeAsync();
            if (File.Exists(copyPath)) File.Delete(copyPath);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _cleanUp();
}
