using Microsoft.Extensions.Options;
using Tankstat.Application.Health;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

public sealed record VehicleDefaults(DistanceUnit DistanceUnit, VolumeUnit VolumeUnit, string Currency);

public sealed class Query
{
    public Task<HealthReport> GetHealth([Service] HealthReporter health, CancellationToken ct) =>
        health.ReportAsync(ct);

    /// <summary>One page of the vehicles the user may see, sorted by the server.</summary>
    public Task<IReadOnlyList<Vehicle>> GetVehicles(
        [Service] VehicleService vehicles, CancellationToken ct,
        VehicleSortField orderBy = VehicleSortField.Name, SortDirection direction = SortDirection.Asc,
        int skip = 0, int take = VehicleQuery.DefaultTake) =>
        vehicles.ListAsync(new VehicleQuery(orderBy, direction, skip, take), ct);

    /// <summary>Total number of vehicles the user may see (for paging).</summary>
    public Task<int> GetVehicleCount([Service] VehicleService vehicles, CancellationToken ct) => vehicles.CountAsync(ct);

    /// <summary>One page of the trashed vehicles the user may restore or permanently delete, sorted by the server.</summary>
    public Task<IReadOnlyList<Vehicle>> GetTrash(
        [Service] VehicleService vehicles, CancellationToken ct,
        VehicleSortField orderBy = VehicleSortField.DeletedAt, SortDirection direction = SortDirection.Desc,
        int skip = 0, int take = VehicleQuery.DefaultTake) =>
        vehicles.ListTrashAsync(new VehicleQuery(orderBy, direction, skip, take), ct);

    public Task<int> GetTrashCount([Service] VehicleService vehicles, CancellationToken ct) => vehicles.CountTrashAsync(ct);

    /// <summary>How many trashed vehicles the user may delete permanently (owners, administrators and Delete grants).</summary>
    public Task<int> GetTrashDeletableCount([Service] VehicleService vehicles, CancellationToken ct) => vehicles.CountDeletableTrashAsync(ct);

    /// <summary>The units suggested for a new vehicle (set per instance, e.g. miles and gallons in the US).</summary>
    public VehicleDefaults GetVehicleDefaults([Service] IOptions<VehicleDefaultsOptions> defaults) =>
        new(defaults.Value.DistanceUnit, defaults.Value.VolumeUnit, defaults.Value.Currency);

    public Task<Vehicle?> GetVehicle(Guid id, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.FindAsync(id, ct);
}
