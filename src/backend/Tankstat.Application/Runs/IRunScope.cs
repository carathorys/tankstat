using Tankstat.Application.Auth;

namespace Tankstat.Application.Runs;

/// <summary>Who starts a run and the cars it will touch (#335).</summary>
public sealed record RunStart(Principal Starter, IReadOnlyCollection<Guid> VehicleIds);

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
