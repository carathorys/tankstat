using Tankstat.Application.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Refuelings;

public interface IRefuelingRepository
{
    /// <summary>One page of a vehicle's logs that are not in the trash.</summary>
    Task<IReadOnlyList<Refueling>> ListForVehicleAsync(Guid vehicleId, RefuelingQuery query, CancellationToken ct);
    Task<int> CountForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>Whether the vehicle has any log, trashed ones included (its units are locked then).</summary>
    Task<bool> AnyForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>Every log of the vehicle that is not in the trash (date and odometer are loaded), to recognise duplicates when importing.</summary>
    Task<IReadOnlyList<Refueling>> ListAllForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>Writes only the consumption of these logs (nothing else about them changes).</summary>
    Task SaveConsumptionsAsync(IReadOnlyList<Refueling> refuelings, CancellationToken ct);

    /// <summary>One page of the trashed logs in scope (across vehicles).</summary>
    Task<IReadOnlyList<Refueling>> ListDeletedAsync(OwnerScope scope, RefuelingQuery query, CancellationToken ct);
    Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct);

    Task<Refueling?> FindAsync(Guid id, CancellationToken ct);
    Task<Refueling?> FindIncludingDeletedAsync(Guid id, CancellationToken ct);

    Task AddAsync(Refueling refueling, CancellationToken ct);
    Task UpdateAsync(Refueling refueling, CancellationToken ct);

    /// <summary>Physically removes the trashed logs in scope. Returns how many.</summary>
    Task<int> PurgeAsync(OwnerScope scope, CancellationToken ct);
}
