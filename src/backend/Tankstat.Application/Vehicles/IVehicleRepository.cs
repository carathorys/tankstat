using Tankstat.Application.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Vehicles;

public interface IVehicleRepository
{
    /// <summary>Vehicles that are not in the trash.</summary>
    Task<IReadOnlyList<Vehicle>> ListAsync(OwnerScope scope, CancellationToken ct);

    /// <summary>Vehicles in the trash, most recently deleted first.</summary>
    Task<IReadOnlyList<Vehicle>> ListDeletedAsync(OwnerScope scope, CancellationToken ct);

    /// <summary>Finds a vehicle that is not in the trash.</summary>
    Task<Vehicle?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>Finds a vehicle whether or not it is in the trash.</summary>
    Task<Vehicle?> FindIncludingDeletedAsync(Guid id, CancellationToken ct);

    Task AddAsync(Vehicle vehicle, CancellationToken ct);
    Task UpdateAsync(Vehicle vehicle, CancellationToken ct);

    /// <summary>Physically removes the trashed vehicles (and their refuelings) in scope. Returns how many vehicles were removed.</summary>
    Task<int> PurgeAsync(OwnerScope scope, CancellationToken ct);
}
