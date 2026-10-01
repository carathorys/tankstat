using Tankstat.Domain.Charts;

namespace Tankstat.Application.Stats;

public interface IStatsRepository
{
    /// <summary>All live fill-ups, expenses and odometer readings of a vehicle, reduced to what statistics need.</summary>
    Task<StatsData> LoadAsync(Guid vehicleId, CancellationToken ct);
}

public interface IVehicleChartRepository
{
    /// <summary>The vehicle's charts that are shared, plus the ones created by <paramref name="userId"/>, oldest first.</summary>
    Task<IReadOnlyList<VehicleChart>> ListVisibleAsync(Guid vehicleId, Guid userId, CancellationToken ct);
    Task<int> CountByUserAsync(Guid vehicleId, Guid userId, CancellationToken ct);
    Task<VehicleChart?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(VehicleChart chart, CancellationToken ct);
    Task UpdateAsync(VehicleChart chart, CancellationToken ct);
    Task RemoveAsync(VehicleChart chart, CancellationToken ct);
}
