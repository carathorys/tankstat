using Tankstat.Application.Health;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

public sealed class Query
{
    public Task<HealthReport> GetHealth([Service] HealthReporter health, CancellationToken ct) =>
        health.ReportAsync(ct);

    public Task<IReadOnlyList<Vehicle>> GetVehicles([Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.ListAsync(ct);

    /// <summary>Vehicles in the trash that the user may restore or permanently delete.</summary>
    public Task<IReadOnlyList<Vehicle>> GetTrash([Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.ListTrashAsync(ct);

    public Task<Vehicle?> GetVehicle(Guid id, [Service] VehicleService vehicles, CancellationToken ct) =>
        vehicles.FindAsync(id, ct);
}
