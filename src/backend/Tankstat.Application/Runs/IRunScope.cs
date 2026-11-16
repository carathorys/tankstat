using Tankstat.Application.Auth;

namespace Tankstat.Application.Runs;

/// <summary>Who starts a run and the cars it will touch (#335).</summary>
/// <param name="RunId">The run's id, when whoever starts it keeps a record of its own under it (#337); else one is made.</param>
public sealed record RunStart(Principal Starter, IReadOnlyCollection<Guid> VehicleIds, Guid? RunId = null);

/// <summary>
/// A run (#335): background work, an import or a partial restore, whose every save through the app's services lands in one database
/// transaction. Anything the work throws rolls the whole run back, and so does a server that stops; a part of it (a car, an entry) can be
/// rolled back alone through <see cref="IRun.InSavepointAsync"/>.
/// </summary>
public interface IRunScope
{
    /// <summary>
    /// Runs <paramref name="work"/> in one transaction and commits when it returns. Inside it the services act as the run's starter
    /// (<see cref="RunContext"/>), without a request. A run inside a run is refused.
    /// </summary>
    Task RunAsync(RunStart start, Func<IRun, Task> work, CancellationToken ct);

    /// <summary>
    /// Asks an open run to stop (the owner's decision 3): its starter and any administrator may. The run sees it between its entries
    /// (<see cref="IRun.ThrowIfStoppedAsync"/>), rolls back and ends as cancelled. On this server at once; a run of another server through
    /// its mark, which SQLite cannot write while a run is open (there one server has the database to itself). False when no such run is open.
    /// </summary>
    Task<bool> CancelAsync(Guid runId, Principal by, CancellationToken ct);
}

/// <summary>
/// A run was stopped before it finished, cancelled or past its limit (<see cref="Tankstat.Domain.Runs.RunEnd"/>): thrown by
/// <see cref="IRun.ThrowIfStoppedAsync"/> through the work, so the run rolls back whole, and rethrown to whoever started it.
/// </summary>
public sealed class RunStoppedException(Tankstat.Domain.Runs.RunEnd reason) : Exception($"The run was stopped: {reason}.")
{
    public Tankstat.Domain.Runs.RunEnd Reason { get; } = reason;
}

/// <summary>What the work of a run is handed.</summary>
public interface IRun
{
    /// <summary>The run's id: its mark's (<see cref="IRunMarkerRepository"/>).</summary>
    Guid Id { get; }

    /// <summary>
    /// Runs <paramref name="work"/> in a savepoint of its own, which may hold further ones (a car, an entry inside it). When it throws,
    /// everything it saved is rolled back, the run goes on, and the exception reaches the caller, who decides what a refusal means.
    /// </summary>
    Task InSavepointAsync(Func<Task> work);

    /// <summary>
    /// For the work to call between its entries: throws <see cref="RunStoppedException"/> once the run was cancelled or has held its
    /// transaction longer than the limit (<c>Runs:LimitMinutes</c>). Nothing stops a run in the middle of a save.
    /// </summary>
    Task ThrowIfStoppedAsync();

    /// <summary>
    /// Something outside the database to take back (a photo's file written for a row of this run): run when what was saved around it is
    /// rolled back, the savepoint it was registered in or the whole run.
    /// </summary>
    void OnRollback(Func<Task> undo);

    /// <summary>
    /// Something outside the database that must wait for the commit (the old picture's file deleted): run after the run committed, never
    /// when the savepoint it was registered in, or the run, was rolled back.
    /// </summary>
    void AfterCommit(Func<Task> act);
}
