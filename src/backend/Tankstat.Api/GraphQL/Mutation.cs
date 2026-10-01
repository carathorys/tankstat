using Microsoft.Extensions.Options;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

public sealed record MeasurementUnitsInput(DistanceUnit Distance, VolumeUnit Volume)
{
    public MeasurementUnits ToDomain() => MeasurementUnits.Create(Distance, Volume);
}

/// <param name="Units">Omit to use the instance defaults (see <c>vehicleDefaults</c>).</param>
public sealed record AddVehicleInput(string Name, string? LicensePlate, FuelType FuelType, MeasurementUnitsInput? Units);

/// <param name="Units">Omit to keep the vehicle's units. They can only change while it has no logs.</param>
public sealed record UpdateVehicleInput(Guid Id, string Name, string? LicensePlate, FuelType FuelType, MeasurementUnitsInput? Units);

public sealed class Mutation
{
    public Task<Vehicle> AddVehicle(
        AddVehicleInput input, [Service] VehicleService vehicles, [Service] IOptions<VehicleDefaultsOptions> defaults, CancellationToken ct) =>
        vehicles.AddAsync(input.Name, input.LicensePlate, input.FuelType,
            input.Units?.ToDomain() ?? MeasurementUnits.Create(defaults.Value.DistanceUnit, defaults.Value.VolumeUnit), ct);

    public Task<Vehicle> UpdateVehicle(UpdateVehicleInput input, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.UpdateAsync(input.Id, input.Name, input.LicensePlate, input.FuelType, input.Units?.ToDomain(), ct);

    /// <summary>Moves the vehicle to the trash (logical deletion).</summary>
    public Task<Vehicle> DeleteVehicle(Guid id, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.DeleteAsync(id, ct);

    public Task<Vehicle> RestoreVehicle(Guid id, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.RestoreAsync(id, ct);

    /// <summary>Permanently removes the trashed vehicles the user has Delete access to; returns how many vehicles were removed.</summary>
    public Task<int> EmptyTrash([Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.EmptyTrashAsync(ct);
}
