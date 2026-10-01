using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Sharing;

public sealed record LogAccessEntry(User User, AccessLevel Level);

/// <summary>
/// Lets whoever may edit a vehicle (its owner, administrators, editors) give other users access to that vehicle's logs
/// without giving access to the rest of the owner's data. Nobody can hand out more than they hold themselves.
/// </summary>
public sealed class ResourceSharingService(
    IVehicleRepository vehicles, IResourceGrantRepository grants, IUserRepository users, AccessService access)
{
    public async Task<IReadOnlyList<LogAccessEntry>> ListLogAccessAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await ManageableVehicleAsync(vehicleId, ct);
        var entries = new List<LogAccessEntry>();
        foreach (var g in await grants.ListForResourceAsync(ResourceType.Vehicle, vehicle.Id, ct))
            if (g.Feature == GrantedFeature.Logs && await users.FindByIdAsync(g.GranteeId, ct) is { } user) entries.Add(new LogAccessEntry(user, g.Level));
        return entries.OrderBy(e => e.User.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Users the vehicle's logs could still be shared with (not the owner, nobody who already has a grant, not the current user).</summary>
    public async Task<IReadOnlyList<User>> CandidatesAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await ManageableVehicleAsync(vehicleId, ct);
        var me = await access.RequirePrincipalAsync(ct);
        var taken = (await grants.ListForResourceAsync(ResourceType.Vehicle, vehicle.Id, ct)).Select(g => g.GranteeId).ToHashSet();
        return (await users.ListAsync(ct)).Where(u => !u.IsDisabled && u.Id != vehicle.OwnerId && u.Id != me.Id && !taken.Contains(u.Id)).ToList();
    }

    /// <summary>Sets the user's access to the vehicle's logs: Edit or Delete; <see cref="AccessLevel.None"/> revokes it.</summary>
    public async Task SetLogAccessAsync(Guid vehicleId, Guid userId, AccessLevel level, CancellationToken ct)
    {
        var vehicle = await ManageableVehicleAsync(vehicleId, ct);
        if (userId == vehicle.OwnerId) throw new DomainException("share.ownerHasAccess", "The owner always has full access.");
        if (await users.FindByIdAsync(userId, ct) is null) throw new NotFoundException("user.notFound", $"User {userId} does not exist.", new { Id = userId });

        var existing = await grants.FindAsync(ResourceType.Vehicle, vehicle.Id, userId, GrantedFeature.Logs, ct);
        if (level == AccessLevel.None)
        {
            if (existing is not null) await grants.RemoveAsync(existing, ct);
            return;
        }

        // You cannot give away more than you hold (an editor cannot hand out permanent-delete rights).
        if (level > await access.LogLevelAsync(vehicle, ct)) throw new ForbiddenException("share.cannotGrantMore", "You cannot grant more access than you have.");

        if (existing is null) await grants.AddAsync(ResourceGrant.Create(ResourceType.Vehicle, vehicle.Id, userId, GrantedFeature.Logs, level), ct);
        else
        {
            existing.ChangeLevel(level);
            await grants.UpdateAsync(existing, ct);
        }
    }

    private async Task<Vehicle> ManageableVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var level = vehicle is null ? AccessLevel.None : await access.VehicleLevelAsync(vehicle, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId });
        if (level < AccessLevel.Edit) throw new ForbiddenException("share.editRequired", "You need edit access to the vehicle to share it.");
        return vehicle;
    }
}
