namespace Tankstat.Domain.Runs;

/// <summary>How a run ended.</summary>
public enum RunEnd
{
    /// <summary>Committed: everything it saved stays.</summary>
    Done,
    /// <summary>Something unexpected: rolled back.</summary>
    Failed,
    /// <summary>Cancelled by its starter or an administrator: rolled back.</summary>
    Cancelled,
    /// <summary>It held the transaction longer than the limit: rolled back.</summary>
    TimedOut,
    /// <summary>The server stopped while it ran, so the database rolled it back; found without an end afterwards.</summary>
    Interrupted,
}

/// <summary>
/// The mark of a run (#335), saved before its transaction opens and ended after it, both outside the transaction: every server sees which
/// cars a run holds (their saves wait), and a run that left no other trace, rolled back by a server that stopped, is still known to have
/// existed. It names users and cars by id without a foreign key: a run may create its cars, and the mark outlives both.
/// </summary>
public sealed class RunMarker
{
    private readonly List<RunMarkerVehicle> _vehicles = [];

    private RunMarker() { } // EF Core

    public Guid Id { get; private set; }

    public Guid StartedById { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Null while the run is open.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    public RunEnd? End { get; private set; }

    /// <summary>Someone asked for the run to be cancelled; the run looks between its entries.</summary>
    public DateTimeOffset? CancelRequestedAt { get; private set; }

    public Guid? CancelRequestedById { get; private set; }

    /// <summary>The cars whose saves wait while the run is open.</summary>
    public IReadOnlyList<RunMarkerVehicle> Vehicles => _vehicles;

    public bool IsOpen => EndedAt is null;

    public static RunMarker Start(Guid startedById, IEnumerable<Guid> vehicleIds, DateTimeOffset now, Guid? id = null)
    {
        var marker = new RunMarker { Id = id ?? Guid.NewGuid(), StartedById = startedById, StartedAt = now };
        marker._vehicles.AddRange(vehicleIds.Distinct().Select(v => new RunMarkerVehicle(marker.Id, v)));
        return marker;
    }
}

/// <summary>A car a run holds.</summary>
public sealed class RunMarkerVehicle
{
    private RunMarkerVehicle() { } // EF Core

    internal RunMarkerVehicle(Guid runId, Guid vehicleId)
    {
        RunId = runId;
        VehicleId = vehicleId;
    }

    public Guid RunId { get; private set; }

    public Guid VehicleId { get; private set; }
}
