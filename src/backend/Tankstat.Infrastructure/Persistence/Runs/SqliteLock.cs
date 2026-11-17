using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tankstat.Application.Runs;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// SQLite while a run is open (#335): the run's transaction holds the database's one lock to write, so every other save waits. The
/// driver already retries a locked database until the command's timeout (30 seconds unless set); while a run of this process is open
/// that timeout is the gate's wait, about ten seconds, for every command outside the run. A save that still finds the database locked is
/// answered as busy (<see cref="AppDbContext"/>, and <see cref="IsLocked"/> for whoever catches it elsewhere), never as a raw database error.
/// A SQLite file has one server, so what this process knows is all there is.
/// </summary>
internal sealed class SqliteLock(RunTimings timings) : DbCommandInterceptor
{
    private static int _open;

    /// <summary>A run of this process is open on SQLite.</summary>
    public static bool Held => Volatile.Read(ref _open) > 0;

    public static void Enter() => Interlocked.Increment(ref _open);

    public static void Leave() => Interlocked.Decrement(ref _open);

    /// <summary>Whether the failure is SQLite finding the database locked or busy, however deep it is wrapped.</summary>
    public static bool IsLocked(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
            if (e is SqliteException { SqliteErrorCode: 5 or 6 }) return true; // SQLITE_BUSY, SQLITE_LOCKED
        return false;
    }

    /// <summary>A failure to answer as busy: the database locked while a run is open, seen from outside that run.</summary>
    public static bool IsBusy(Exception e) => Held && RunTransaction.Current is null && IsLocked(e);

    private void Shorten(DbCommand command)
    {
        // 0 would wait for ever. The run's own commands are never locked out, and keep their own time.
        if (Held && RunTransaction.Current is null) command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(timings.BusyWait.TotalSeconds));
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Shorten(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        Shorten(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Shorten(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Shorten(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Shorten(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
    {
        Shorten(command);
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// The same for a transaction's start: a save begins one, and there SQLite waits by the connection's own timeout, not a command's. The
/// connections are pooled, so the timeout is set either way each time: the gate's wait while a run is open, the connection string's own otherwise.
/// </summary>
internal sealed class SqliteLockTransactions(RunTimings timings) : DbTransactionInterceptor
{

    private void Set(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite) return;
        var waits = SqliteLock.Held && RunTransaction.Current is null;
        // Otherwise the connection string's own (Default Timeout; the driver's 30 seconds without one).
        sqlite.DefaultTimeout = waits
            ? Math.Max(1, (int)Math.Ceiling(timings.BusyWait.TotalSeconds))
            : new SqliteConnectionStringBuilder(sqlite.ConnectionString).DefaultTimeout;
    }

    public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        Set(connection);
        return result;
    }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken ct = default)
    {
        Set(connection);
        return ValueTask.FromResult(result);
    }
}

internal sealed class BusyFailures : IBusyFailures
{
    public bool IsBusy(Exception failure) => SqliteLock.IsBusy(failure);
}
