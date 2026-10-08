using Tankstat.Api.Media;
using Tankstat.Application.Access;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

/// <summary>The vehicle type hides the internal picture id; clients use <c>pictureUrl</c>.</summary>
public sealed class VehicleType : ObjectType<Vehicle>
{
    protected override void Configure(IObjectTypeDescriptor<Vehicle> descriptor)
    {
        descriptor.Ignore(v => v.PictureImageId);
        descriptor.Ignore(v => v.SavedVersion); // the persistence layer's, not a client's
    }
}

/// <summary>Fields of <c>Vehicle</c> that depend on who is asking, or that live in other aggregates.</summary>
[ExtendObjectType<Vehicle>]
public sealed class VehicleExtensions
{
    /// <summary>Whether the current user may edit (and trash) this vehicle itself; the UI hides the actions otherwise.</summary>
    public async Task<bool> GetCanEdit([Parent] Vehicle vehicle, [Service] AccessService access, CancellationToken ct) =>
        await access.LevelAsync(vehicle.OwnerId, ct) >= AccessLevel.Edit;

    /// <summary>What the current user may do with this vehicle's logs: view, add/change (Edit) or also delete permanently (Delete).</summary>
    public Task<AccessLevel> GetLogAccess([Parent] Vehicle vehicle, [Service] RefuelingService refuelings, CancellationToken ct) =>
        refuelings.LevelForVehicleAsync(vehicle.Id, ct);

    /// <summary>Null when authentication is off or the owner no longer exists.</summary>
    public async Task<UserRef?> GetOwner([Parent] Vehicle vehicle, [Service] IUserRepository users, CancellationToken ct) =>
        await users.FindByIdAsync(vehicle.OwnerId, ct) is { } owner ? UserRef.From(owner) : null;

    /// <summary>Address of the vehicle's picture, if it has one.</summary>
    public string? GetPictureUrl([Parent] Vehicle vehicle) => MediaUrls.Image(vehicle.PictureImageId);

    public Task<int> GetRefuelingCount([Parent] Vehicle vehicle, [Service] RefuelingService refuelings, CancellationToken ct) =>
        refuelings.CountAsync(vehicle.Id, ct);
}
