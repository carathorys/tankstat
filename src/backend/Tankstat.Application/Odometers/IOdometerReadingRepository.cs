using Tankstat.Domain.Odometers;

namespace Tankstat.Application.Odometers;

/// <summary>Queries over all odometer readings of a vehicle, whichever entity recorded them. Trashed readings are ignored.</summary>
public interface IOdometerReadingRepository
{
    /// <summary>The latest reading dated strictly before <paramref name="date"/>, ignoring <paramref name="exceptReadingId"/> (the one being edited).</summary>
    Task<OdometerReading?> PreviousAsync(Guid vehicleId, DateOnly date, Guid? exceptReadingId, CancellationToken ct);

    /// <summary>The earliest reading dated strictly after <paramref name="date"/>.</summary>
    Task<OdometerReading?> NextAsync(Guid vehicleId, DateOnly date, Guid? exceptReadingId, CancellationToken ct);

    /// <summary>The most recent reading (by date, then value), for pre-filling forms.</summary>
    Task<OdometerReading?> LatestAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>The most recent reading of each of the vehicles in one query; vehicles without a reading are missing from the result.</summary>
    Task<IReadOnlyDictionary<Guid, OdometerReading>> LatestForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct);

    /// <summary>Whether the vehicle has any reading at all, trashed ones included.</summary>
    Task<bool> AnyAsync(Guid vehicleId, CancellationToken ct);
}
