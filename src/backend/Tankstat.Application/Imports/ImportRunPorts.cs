using Tankstat.Domain.Imports;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>The runs' rows (#337). Inside a run the saves are part of its transaction (the report commits with the cars); the others stand on their own.</summary>
public interface IImportRunRepository
{
    Task AddAsync(ImportRun run, CancellationToken ct);
    Task<ImportRun?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>The runs that go or wait, the oldest first.</summary>
    Task<IReadOnlyList<ImportRun>> ListActiveAsync(CancellationToken ct);

    /// <summary>The person's run that goes or waits, else their latest one that ended.</summary>
    Task<ImportRun?> LatestOfAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Gives the run its turn: waiting to running, only when no run is running and none waits longer. One statement, so two servers
    /// asking at once cannot both be given the turn for the same run; false when it is not this run's turn.
    /// </summary>
    Task<bool> ClaimAsync(Guid id, DateTimeOffset now, CancellationToken ct);

    /// <summary>Done, with its report: called inside the run, as its last save.</summary>
    Task CompleteAsync(Guid id, string report, int cars, int entries, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Ends a run that brought nothing in; one that ended already stays as it is. False when it had ended, or, with
    /// <paramref name="waitingOnly"/>, when it no longer waits: whoever ends a run because it waits (a cancel, the wait's limit, a file
    /// that is gone) must not end one that was given its turn since they looked.
    /// </summary>
    Task<bool> FailAsync(Guid id, ImportRunFailure failure, Guid? cancelledById, DateTimeOffset now, CancellationToken ct, bool waitingOnly = false);

    /// <summary>Says how far a running run is. Never called on a database that the run locks.</summary>
    Task ProgressAsync(Guid id, int doneCars, int doneEntries, CancellationToken ct);

    /// <summary>Says the server still holds its waiting and running runs.</summary>
    Task SeenAsync(string server, DateTimeOffset now, CancellationToken ct);

    /// <summary>Removes the ended runs older than <paramref name="endedBefore"/>.</summary>
    Task<int> PruneAsync(DateTimeOffset endedBefore, CancellationToken ct);
}

/// <summary>
/// The cars that are here, read for the import: the person's own (the targets), and a target's entries in the shapes a backup's are
/// read into, live and in the trash, so they can be compared. Inside a run the reads are the run's own.
/// </summary>
public interface IImportTargetReader
{
    /// <summary>The cars the person owns that are not in the trash, by name.</summary>
    Task<IReadOnlyList<Vehicle>> OwnedAsync(Guid ownerId, CancellationToken ct);

    /// <summary>The car's refuellings, expenses and schedules as they are now.</summary>
    Task<BackupCar> LiveAsync(Vehicle vehicle, CancellationToken ct);

    /// <summary>The car's refuellings and expenses that are in the trash (schedules have none).</summary>
    Task<BackupCar> TrashAsync(Vehicle vehicle, CancellationToken ct);

    /// <summary>
    /// What holds a <i>Replace all</i> back (#338, the owner's decision 2): how many of the car's refuellings and expenses, live or in the
    /// trash, have photos, and how many of its expenses belong to a tyre log line.
    /// </summary>
    Task<ImportHeldBack> HeldBackAsync(Vehicle vehicle, CancellationToken ct);
}

/// <summary>The entries of a car that a backup cannot bring back yet, per kind: counted, never listed.</summary>
public sealed record ImportHeldBack(int RefuelingPhotos, int ExpensePhotos, int ExpenseTyreLines);

/// <summary>The server's side of the queue (#337): it holds the files of the runs it was given and starts each when its turn comes.</summary>
public interface IBackupImportRunner
{
    /// <summary>Why no run can be started on this server's database (a refusal's key), or null where it can (#334's T14: MySQL until #345 is fixed).</summary>
    string? UnavailableKey { get; }

    /// <summary>This server process's name in the runs' rows.</summary>
    string Server { get; }

    /// <summary>Takes a queued run: the session it reads is held until the run ends. Then looks whose turn it is.</summary>
    void Enqueue(Guid runId, Guid userId, string sessionToken);

    /// <summary>How far a run of this server is, when it is running here.</summary>
    ImportRunProgress? ProgressOf(Guid runId);

    /// <summary>Whether the run's file is still open on this server (to go back to the review after a run that brought nothing in).</summary>
    string? SessionOf(Guid runId);

    /// <summary>A waiting run of this server was cancelled: its file is let go.</summary>
    void Dropped(Guid runId);

    /// <summary>Someone asked a run of this server to cancel: who, for when it ends.</summary>
    void Cancelling(Guid runId, Guid by);

    /// <summary>Looks whose turn it is now.</summary>
    void Wake();
}
