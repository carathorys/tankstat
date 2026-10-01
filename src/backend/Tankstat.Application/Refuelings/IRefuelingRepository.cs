using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Refuelings;

public interface IRefuelingRepository
{
    /// <summary>Newest first (by date, then odometer).</summary>
    Task<IReadOnlyList<Refueling>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct);
    Task<int> CountForVehicleAsync(Guid vehicleId, CancellationToken ct);
    Task AddAsync(Refueling refueling, CancellationToken ct);
}
