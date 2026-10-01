using Tankstat.Application.Access;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

/// <summary>Adds the <c>refuelings</c> field to <c>Vehicle</c> without making the domain entity navigate to them.</summary>
[ExtendObjectType<Vehicle>]
public sealed class VehicleExtensions
{
    /// <summary>Whether the current user may edit or delete this vehicle (the UI hides the actions otherwise).</summary>
    public Task<bool> GetCanEdit([Parent] Vehicle vehicle, [Service] AccessService access, CancellationToken ct) =>
        access.CanAsync(vehicle.OwnerId, AccessLevel.Edit, ct);

    /// <summary>Display name of the owner; null when auth is off or the owner is gone.</summary>
    public async Task<string?> GetOwnerName([Parent] Vehicle vehicle, [Service] IUserRepository users, CancellationToken ct) =>
        (await users.FindByIdAsync(vehicle.OwnerId, ct))?.DisplayName;

    public Task<int> GetRefuelingCount([Parent] Vehicle vehicle, [Service] RefuelingService refuelings, CancellationToken ct) =>
        refuelings.CountForVehicleAsync(vehicle.Id, ct);

    public Task<IReadOnlyList<Refueling>> GetRefuelings(
        [Parent] Vehicle vehicle, [Service] RefuelingService refuelings, CancellationToken ct) =>
        refuelings.ListForVehicleAsync(vehicle.Id, ct);
}
