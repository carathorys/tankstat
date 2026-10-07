using Tankstat.Application.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Vehicles;

public interface IVehicleRepository
{
    /// <summary>One page of the vehicles that are not in the trash, ordered by the database.</summary>
    Task<IReadOnlyList<Vehicle>> ListAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct);

    /// <summary>How many vehicles are not in the trash (for paging).</summary>
    Task<int> CountAsync(OwnerScope scope, string? search, CancellationToken ct);

    /// <summary>One page of the vehicles in the trash, ordered by the database.</summary>
    Task<IReadOnlyList<Vehicle>> ListDeletedAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct);

    Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct);

    /// <summary>Finds a vehicle that is not in the trash.</summary>
    Task<Vehicle?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>The (not trashed) vehicles with these ids, in no particular order; unknown ids are skipped.</summary>
    Task<IReadOnlyList<Vehicle>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Finds a vehicle whether or not it is in the trash.</summary>
    Task<Vehicle?> FindIncludingDeletedAsync(Guid id, CancellationToken ct);

    /// <summary>The vehicles with these ids, trashed ones too, in one query (lists of logs: each row's vehicle).</summary>
    Task<IReadOnlyList<Vehicle>> ListByIdsIncludingDeletedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>The (live) vehicle whose picture is the given image.</summary>
    Task<Vehicle?> FindByPictureImageAsync(Guid imageId, CancellationToken ct);

    /// <summary>Saves a new vehicle; false when one with its id already existed (a concurrent add with the same client id won).</summary>
    Task<bool> AddAsync(Vehicle vehicle, CancellationToken ct);
    Task UpdateAsync(Vehicle vehicle, CancellationToken ct);

    /// <summary>Physically removes the trashed vehicles (and their logs) in scope.</summary>
    Task<PurgeResult> PurgeAsync(OwnerScope scope, CancellationToken ct);
}

/// <summary>What a permanent deletion removed: how many vehicles, and which pictures are now orphaned and must be deleted too.</summary>
/// <summary>What a purge removed: the vehicles' ids (their upload folders go too) and their pictures' ids (for pictures stored before folders existed).</summary>
public sealed record PurgeResult(int Count, IReadOnlyList<Guid> ImageIds, IReadOnlyList<Guid> VehicleIds);
