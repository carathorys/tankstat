using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Stats;

/// <summary>
/// Statistics of a vehicle's logs (charts and key figures). Anyone who may view the vehicle's logs may see them; for everyone else the
/// vehicle does not exist.
/// </summary>
public sealed class StatsService(IVehicleRepository vehicles, IStatsRepository stats, AccessService access, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<ChartData> ChartAsync(Guid vehicleId, ChartConfig config, CancellationToken ct)
    {
        config = config.Validated();
        await RequireVisibleAsync(vehicleId, ct);
        return StatsCalculator.Chart(config, await stats.LoadAsync(vehicleId, ct), Today);
    }

    /// <summary>Null when the vehicle is not visible.</summary>
    public async Task<VehicleSummary?> SummaryAsync(Guid vehicleId, CancellationToken ct) =>
        await IsVisibleAsync(vehicleId, ct) ? StatsCalculator.Summary(await stats.LoadAsync(vehicleId, ct), Today) : null;

    private async Task<bool> IsVisibleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        return vehicle is not null && await access.LogLevelAsync(vehicle, ct) >= AccessLevel.View;
    }

    private async Task RequireVisibleAsync(Guid vehicleId, CancellationToken ct)
    {
        if (!await IsVisibleAsync(vehicleId, ct)) throw new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId });
    }
}
