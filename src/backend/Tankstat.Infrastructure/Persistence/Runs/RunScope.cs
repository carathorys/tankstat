using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Runs;
using Tankstat.Domain.Runs;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// A run's one transaction (#335): a connection of its own, opened and begun here, shared with every context the services lease while
/// the work runs (<see cref="RunDbContextFactory"/>). The work's exception rolls everything back and is rethrown.
/// </summary>
internal sealed class RunScope(
    DbContextOptions<AppDbContext> options, IRunMarkerRepository markers, RunGateState gate, IOptionsMonitor<RunOptions> settings, RunTimings timings, TimeProvider clock,
    IOptions<DatabaseOptions> database, ILogger<RunScope> logger) : IRunScope
{
    /// <summary>
    /// The runs open on this server, to cancel them without the database: from the mark on, so a cancel asked for before the transaction
    /// opens (the settle, a connection waited for) is kept for the run, which SQLite could not be told through its mark.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Stop> _open = new();

    public async Task RunAsync(RunStart start, Func<IRun, Task> work, CancellationToken ct)
    {
        if (RunTransaction.Current is not null) throw new InvalidOperationException("A run cannot be started inside a run.");

        // The mark first, outside the transaction: saves to these cars wait from here on, on every server, and a run a stopped server
        // rolled back is still known to have existed.
        var marker = RunMarker.Start(start.Starter.Id, start.VehicleIds, clock.GetUtcNow());
        await markers.AddAsync(marker, ct);
        var stop = new Stop();
        _open[marker.Id] = stop;
        gate.OpenedHere(marker.Id, start.VehicleIds);
        gate.Forget();
        logger.LogInformation("Run {RunId} started by {UserId} for {VehicleCount} vehicle(s)", marker.Id, start.Starter.Id, marker.Vehicles.Count);
        var end = RunEnd.Failed;
        try
        {
            // Saves that passed the gate just before the mark finish meanwhile, and another server's gate sees the mark.
            if (timings.Settle > TimeSpan.Zero) await Task.Delay(timings.Settle, clock, ct);
            // Cancelled meanwhile: nothing is opened for it (work that never asks would otherwise run to its end).
            if (stop.Cancelled) throw new RunStoppedException(RunEnd.Cancelled);
            await InTransactionAsync(start, marker, stop, work, ct);
            end = RunEnd.Done;
        }
        catch (RunStoppedException e)
        {
            end = e.Reason;
            throw;
        }
        finally
        {
            _open.TryRemove(marker.Id, out _); // also when the transaction never opened
            // After the transaction, on a connection of its own (on SQLite nothing else could be written while the run was open).
            try
            {
                await markers.EndAsync(marker.Id, end, clock.GetUtcNow(), CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Run {RunId} ended as {RunEnd} but its mark could not be ended; it stops blocking once it is older than the limit", marker.Id, end);
            }
            gate.EndedHere(marker.Id);
            gate.Forget();
            logger.LogInformation("Run {RunId} ended: {RunEnd}", marker.Id, end);
        }
    }

    private async Task InTransactionAsync(RunStart start, RunMarker marker, Stop stop, Func<IRun, Task> work, CancellationToken ct)
    {
        var runId = marker.Id;
        await using var root = new AppDbContext(options); // unpooled: it owns the run's connection, closed with it
        await root.Database.OpenConnectionAsync(ct);
        await using var transaction = await root.Database.BeginTransactionAsync(ct);
        // On SQLite the mark cannot be asked while the run holds the database's lock to write; a cancel there is this server's own.
        // The limit counts from the mark's start, as the gates of other servers count it: they let go of the mark at that moment.
        var run = new Run(runId, transaction, clock, marker.StartedAt + settings.CurrentValue.Limit,
            database.Value.Provider == DatabaseProvider.Sqlite ? null : markers, stop, logger);
        var sqlite = database.Value.Provider == DatabaseProvider.Sqlite;
        if (sqlite)
        {
            SqliteLock.Enter(); // from here every other save waits for this run, then is told busy
            gate.LockedOut(true);
        }
        try
        {
            await InsideAsync(start, new RunTransaction(root.Database.GetDbConnection(), transaction), run, work);
            // Asked to stop before the commit: rolled back. The commit itself is never cut short: one cancelled halfway may have gone
            // through, and the undos below would then take back the files of rows that stayed.
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                // It must not hide why the run failed; the database rolls the transaction back when its connection closes.
                logger.LogDebug(e, "Run {RunId}: its transaction could not be rolled back by hand", runId);
            }
            await run.RolledBackAsync();
            throw;
        }
        finally
        {
            _open.TryRemove(runId, out _);
            if (sqlite)
            {
                SqliteLock.Leave();
                gate.LockedOut(false);
            }
        }
        await run.CommittedAsync();
    }

    public async Task<bool> CancelAsync(Guid runId, Principal by, CancellationToken ct)
    {
        if (await markers.FindAsync(runId, ct) is not { IsOpen: true } marker) return false;
        if (marker.StartedById != by.Id && !by.IsAdmin) throw new ForbiddenException();
        logger.LogInformation("Run {RunId} asked to cancel by {UserId}", runId, by.Id);
        if (_open.TryGetValue(runId, out var here))
        {
            here.Cancelled = true;
            return true;
        }
        // Another server's run: through its mark, which that run asks between its entries.
        return database.Value.Provider != DatabaseProvider.Sqlite && await markers.RequestCancelAsync(runId, by.Id, clock.GetUtcNow(), ct);
    }

    /// <summary>
    /// The ambient values are set in a method of their own: an <see cref="AsyncLocal{T}"/> set in an async method is gone when that method
    /// returns, so the commit, the rollback and the hooks after them run outside the run again.
    /// </summary>
    private static async Task InsideAsync(RunStart start, RunTransaction shared, Run run, Func<IRun, Task> work)
    {
        RunTransaction.Current = shared;
        using var _ = RunContext.Enter(new RunContext(start.Starter, start.VehicleIds));
        await work(run);
    }

    /// <summary>Whether a run was asked to cancel on this server: set by <see cref="CancelAsync"/>, read by the run between its entries.</summary>
    private sealed class Stop
    {
        private volatile bool _cancelled;

        public bool Cancelled
        {
            get => _cancelled;
            set => _cancelled = value;
        }
    }

    private sealed class Run(
        Guid id, IDbContextTransaction transaction, TimeProvider clock, DateTimeOffset until, IRunMarkerRepository? markers, Stop stop, ILogger logger) : IRun
    {
        private static readonly TimeSpan AskEvery = TimeSpan.FromSeconds(1);
        private DateTimeOffset _asked;

        public Guid Id => id;

        public async Task ThrowIfStoppedAsync()
        {
            var now = clock.GetUtcNow();
            if (!stop.Cancelled && markers is not null && now - _asked >= AskEvery)
            {
                _asked = now;
                // Only ever set: a cancel asked for on this server while the mark is being read must not be written over by its answer.
                if (await markers.IsCancelRequestedAsync(id, CancellationToken.None)) stop.Cancelled = true;
            }
            if (stop.Cancelled) throw new RunStoppedException(RunEnd.Cancelled);
            if (now >= until) throw new RunStoppedException(RunEnd.TimedOut);
        }

        /// <summary>The hooks of the savepoint being worked in, the run's own at the bottom.</summary>
        private readonly Stack<Hooks> _levels = new([new Hooks()]);

        public async Task InSavepointAsync(Func<Task> work)
        {
            var savepoint = RunTransaction.NextSavepoint();
            await transaction.CreateSavepointAsync(savepoint);
            _levels.Push(new Hooks());
            try
            {
                await work();
            }
            catch
            {
                var undone = _levels.Pop();
                try
                {
                    await transaction.RollbackToSavepointAsync(savepoint);
                }
                catch
                {
                    _levels.Peek().Take(undone); // not rolled back alone: its hooks follow what is around it, as if it were kept
                    throw;
                }
                await UndoAsync(undone);
                throw;
            }
            // Before the release, so its hooks are not lost when that fails: still to undo if what is around it rolls back, still
            // waiting for the commit.
            var kept = _levels.Pop();
            _levels.Peek().Take(kept);
            await transaction.ReleaseSavepointAsync(savepoint);
        }

        public void OnRollback(Func<Task> undo) => _levels.Peek().Undo.Add(undo);

        public void AfterCommit(Func<Task> act) => _levels.Peek().Deferred.Add(act);

        public Task RolledBackAsync() => UndoAsync(_levels.Peek());

        public async Task CommittedAsync()
        {
            foreach (var act in _levels.Peek().Deferred) await TryAsync(act, "an act after the commit");
        }

        /// <summary>The newest first, as an undo goes.</summary>
        private async Task UndoAsync(Hooks level)
        {
            for (var i = level.Undo.Count - 1; i >= 0; i--) await TryAsync(level.Undo[i], "an undo after a rollback");
        }

        /// <summary>A hook that fails must not hide why the run failed, nor keep the ones after it from running: it is logged, ids only.</summary>
        private async Task TryAsync(Func<Task> act, string step)
        {
            try
            {
                await act();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Run {RunId}: {Step} failed; what it was to take back or finish outside the database may be left behind", id, step);
            }
        }
    }

    private sealed class Hooks
    {
        public List<Func<Task>> Undo { get; } = [];
        public List<Func<Task>> Deferred { get; } = [];

        public void Take(Hooks inner)
        {
            Undo.AddRange(inner.Undo);
            Deferred.AddRange(inner.Deferred);
        }
    }
}
