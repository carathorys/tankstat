using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tankstat.Application.Runs;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// A run's one transaction (#335): a connection of its own, opened and begun here, shared with every context the services lease while
/// the work runs (<see cref="RunDbContextFactory"/>). The work's exception rolls everything back and is rethrown.
/// </summary>
internal sealed class RunScope(DbContextOptions<AppDbContext> options) : IRunScope
{
    public async Task RunAsync(RunStart start, Func<IRun, Task> work, CancellationToken ct)
    {
        if (RunTransaction.Current is not null) throw new InvalidOperationException("A run cannot be started inside a run.");
        await using var root = new AppDbContext(options); // unpooled: it owns the run's connection, closed with it
        await root.Database.OpenConnectionAsync(ct);
        await using var transaction = await root.Database.BeginTransactionAsync(ct);
        var run = new Run(transaction);
        try
        {
            await InsideAsync(start, new RunTransaction(root.Database.GetDbConnection(), transaction), run, work);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await TryAsync(() => transaction.RollbackAsync(CancellationToken.None));
            await run.RolledBackAsync();
            throw;
        }
        await run.CommittedAsync();
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

    private static async Task TryAsync(Func<Task> act)
    {
        try { await act(); }
        catch (Exception) { /* a rollback or an undo that fails must not hide why the run failed */ }
    }

    private sealed class Run(IDbContextTransaction transaction) : IRun
    {
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
                await transaction.RollbackToSavepointAsync(savepoint);
                await undone.RolledBackAsync();
                throw;
            }
            var kept = _levels.Pop();
            await transaction.ReleaseSavepointAsync(savepoint);
            _levels.Peek().Take(kept); // still to undo if what is around it rolls back, still waiting for the commit
        }

        public void OnRollback(Func<Task> undo) => _levels.Peek().Undo.Add(undo);

        public void AfterCommit(Func<Task> act) => _levels.Peek().Deferred.Add(act);

        public Task RolledBackAsync() => _levels.Peek().RolledBackAsync();

        public async Task CommittedAsync()
        {
            foreach (var act in _levels.Peek().Deferred) await TryAsync(act);
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

        /// <summary>The newest first, as an undo goes.</summary>
        public async Task RolledBackAsync()
        {
            for (var i = Undo.Count - 1; i >= 0; i--) await TryAsync(Undo[i]);
        }
    }
}
