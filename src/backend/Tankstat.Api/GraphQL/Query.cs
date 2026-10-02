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

    /// <summary>
    /// One page of the vehicles the user owns or that are shared with them, by name (the home page), optionally only those whose name or
    /// license plate contains <c>search</c>.
    /// </summary>
    public Task<IReadOnlyList<Vehicle>> GetMyVehicles(
        [Service] VehicleService vehicles, CancellationToken ct, string? search = null, int skip = 0, int take = VehicleQuery.DefaultTake) =>
        vehicles.ListMineAsync(search, skip, take, ct);

    /// <summary>How many vehicles <c>myVehicles</c> finds in all, for the same <c>search</c> (the pages add up to this).</summary>
    public Task<int> GetMyVehicleCount([Service] VehicleService vehicles, CancellationToken ct, string? search = null) => vehicles.CountMineAsync(search, ct);

    /// <summary>Administrators only: one page of all the vehicles, sorted by the server.</summary>
    public Task<IReadOnlyList<Vehicle>> GetVehicles(
        [Service] VehicleService vehicles, CancellationToken ct,
        VehicleSortField orderBy = VehicleSortField.Name, SortDirection direction = SortDirection.Asc,
        int skip = 0, int take = VehicleQuery.DefaultTake) =>
        vehicles.ListAsync(new VehicleQuery(orderBy, direction, skip, take), ct);

    /// <summary>Administrators only: total number of vehicles (for paging).</summary>
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
