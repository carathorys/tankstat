using Tankstat.Domain.Runs;

namespace Tankstat.Application.Runs;

/// <summary>
/// The runs' marks (#335). Every call goes around a run's transaction, on a connection of its own: a mark must stay when its run is
/// rolled back. On SQLite nothing can be written here while a run is open (its transaction locks the database), only read.
/// </summary>
public interface IRunMarkerRepository
{
    Task AddAsync(RunMarker marker, CancellationToken ct);

    /// <summary>Ends an open mark; one ended already stays as it is.</summary>
    Task EndAsync(Guid id, RunEnd end, DateTimeOffset at, CancellationToken ct);

    Task<RunMarker?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>Whether any run is open that started after <paramref name="startedAfter"/> (an older one never blocks).</summary>
    Task<bool> AnyOpenAsync(DateTimeOffset startedAfter, CancellationToken ct);

    /// <summary>Whether such a run holds the car.</summary>
    Task<bool> HoldsAsync(Guid vehicleId, DateTimeOffset startedAfter, CancellationToken ct);

    /// <summary>The open marks started before <paramref name="startedBefore"/> (null: all of them), oldest first.</summary>
    Task<IReadOnlyList<RunMarker>> ListOpenAsync(DateTimeOffset? startedBefore, CancellationToken ct);

    /// <summary>Asks an open run to cancel; false when there is none (it ended, or never was).</summary>
    Task<bool> RequestCancelAsync(Guid id, Guid by, DateTimeOffset at, CancellationToken ct);

    Task<bool> IsCancelRequestedAsync(Guid id, CancellationToken ct);

    /// <summary>Removes the marks that ended before <paramref name="endedBefore"/>.</summary>
    Task<int> PruneAsync(DateTimeOffset endedBefore, CancellationToken ct);
}
