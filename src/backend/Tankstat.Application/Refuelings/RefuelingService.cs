using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Refuelings;

public sealed class RefuelingService(IVehicleRepository vehicles, IRefuelingRepository refuelings, AccessService access)
{
    public async Task<IReadOnlyList<Refueling>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        if (vehicle is null || !await access.CanAsync(vehicle.OwnerId, AccessLevel.View, ct)) return [];
        return await refuelings.ListForVehicleAsync(vehicleId, ct);
    }

    public async Task<Refueling> LogAsync(
        Guid vehicleId, DateOnly date, decimal liters, decimal totalCost, int odometerKm, bool isFullTank, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var level = vehicle is null ? AccessLevel.None : await access.LevelAsync(vehicle.OwnerId, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException($"Vehicle {vehicleId} does not exist.");
        if (level < AccessLevel.Edit) throw new ForbiddenException("You may only view this vehicle.");

        var refueling = Refueling.Create(vehicle.OwnerId, vehicleId, date, liters, totalCost, odometerKm, isFullTank);
        await refuelings.AddAsync(refueling, ct);
        return refueling;
    }
}
