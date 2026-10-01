using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

public sealed record AddVehicleInput(string Name, string? LicensePlate, FuelType FuelType);

public sealed record UpdateVehicleInput(Guid Id, string Name, string? LicensePlate, FuelType FuelType);

public sealed record LogRefuelingInput(
    Guid VehicleId, DateOnly Date, decimal Liters, decimal TotalCost, int OdometerKm, bool IsFullTank);

public sealed class Mutation
{
    public Task<Vehicle> AddVehicle(AddVehicleInput input, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.AddAsync(input.Name, input.LicensePlate, input.FuelType, ct);

    public Task<Vehicle> UpdateVehicle(UpdateVehicleInput input, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.UpdateAsync(input.Id, input.Name, input.LicensePlate, input.FuelType, ct);

    /// <summary>Moves the vehicle to the trash (logical deletion).</summary>
    public Task<Vehicle> DeleteVehicle(Guid id, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.DeleteAsync(id, ct);

    public Task<Vehicle> RestoreVehicle(Guid id, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.RestoreAsync(id, ct);

    /// <summary>Permanently removes everything in the trash the user may edit; returns how many vehicles were removed.</summary>
    public Task<int> EmptyTrash([Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.EmptyTrashAsync(ct);

    public Task<Refueling> LogRefueling(LogRefuelingInput input, [Service] RefuelingService refuelings, CancellationToken ct) =>
        refuelings.LogAsync(input.VehicleId, input.Date, input.Liters, input.TotalCost, input.OdometerKm, input.IsFullTank, ct);
}
