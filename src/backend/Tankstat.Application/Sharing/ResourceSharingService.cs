using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Notifications;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Application.Runs;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Sharing;

public sealed record LogAccessEntry(User User, AccessLevel Level);

/// <summary>
/// Lets whoever may edit a vehicle (its owner, administrators, editors) give other users access to that vehicle's logs
/// without giving access to the rest of the owner's data. Nobody can hand out more than they hold themselves. The user whose access
/// changed is notified, and so is the owner when someone else made the change.
/// </summary>
public sealed class ResourceSharingService(
    IVehicleRepository vehicles, IResourceGrantRepository grants, IUserRepository users, AccessService access, Notifier notifier, ILogger<ResourceSharingService> logger)
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
        var grantee = await users.FindByIdAsync(userId, ct) ?? throw new NotFoundException("user.notFound", $"User {userId} does not exist.", new { Id = userId });

        var existing = await grants.FindAsync(ResourceType.Vehicle, vehicle.Id, userId, GrantedFeature.Logs, ct);
        var before = existing?.Level ?? AccessLevel.None;
        if (level == AccessLevel.None)
        {
            if (existing is not null) await grants.RemoveAsync(existing, ct);
        }
        else
        {
            // You cannot give away more than you hold (an editor cannot hand out permanent-delete rights).
            if (level > await access.LogLevelAsync(vehicle, ct)) throw new ForbiddenException("share.cannotGrantMore", "You cannot grant more access than you have.");

            if (existing is null) await grants.AddAsync(ResourceGrant.Create(ResourceType.Vehicle, vehicle.Id, userId, GrantedFeature.Logs, level), ct);
            else
            {
                existing.ChangeLevel(level);
                await grants.UpdateAsync(existing, ct);
            }
        }

        if (before == level) return;
        var actor = await access.RequirePrincipalAsync(ct);
        logger.LogInformation("User {ActorId} set the log access of user {GranteeId} to vehicle {VehicleId} from {Before} to {After}", actor.Id, grantee.Id, vehicle.Id, before, level);
        await NotifyAsync(vehicle, grantee, actor, before, level, ct);
    }

    /// <summary>
    /// A partial restore's way in (#340, <see cref="RunRestore"/>): a gone car's sharing as the backup had it, for the grantees who still
    /// have an account (the caller passes only those), each written as it was. Nobody is told here: the restore's one notification per car
    /// stands for these. Answers how many the car has again.
    /// </summary>
    public async Task<int> RestoreLogAccessAsync(Guid vehicleId, IReadOnlyList<(Guid UserId, AccessLevel Level)> granted, CancellationToken ct)
    {
        RunRestore.Require(await access.RequirePrincipalAsync(ct));
        var vehicle = await ManageableVehicleAsync(vehicleId, ct);
        // A purge leaves a car's grants behind (they name the car without a foreign key). The car comes back with the backup's sharing
        // (#334's T10), exactly: a grant left behind for someone the backup did not share it with goes, one the backup has takes its level.
        var wanted = granted.Where(g => g.UserId != vehicle.OwnerId && g.Level != AccessLevel.None).GroupBy(g => g.UserId).ToDictionary(g => g.Key, g => g.Last().Level);
        foreach (var left in (await grants.ListForResourceAsync(ResourceType.Vehicle, vehicle.Id, ct)).Where(g => g.Feature == GrantedFeature.Logs))
            if (!wanted.TryGetValue(left.GranteeId, out var level)) await grants.RemoveAsync(left, ct);
            else
            {
                if (left.Level != level)
                {
                    left.ChangeLevel(level);
                    await grants.UpdateAsync(left, ct);
                }
                wanted.Remove(left.GranteeId);
            }
        foreach (var (userId, level) in wanted)
            await grants.AddAsync(ResourceGrant.Create(ResourceType.Vehicle, vehicle.Id, userId, GrantedFeature.Logs, level), ct);
        return granted.Where(g => g.UserId != vehicle.OwnerId && g.Level != AccessLevel.None).Select(g => g.UserId).Distinct().Count();
    }

    private async Task NotifyAsync(Vehicle vehicle, User grantee, Principal actor, AccessLevel before, AccessLevel after, CancellationToken ct)
    {
        var car = NotificationRef.Vehicle(vehicle.Id);
        List<NotificationDraft> drafts =
        [
            NotificationArgs.AccessChange(grantee.Id, NotificationKind.LogAccessChanged, car, car, actor.DisplayName, before, after, ("vehicleName", vehicle.Name)),
        ];
        if (actor.Id != vehicle.OwnerId)
            drafts.Add(NotificationArgs.AccessChange(vehicle.OwnerId, NotificationKind.VehicleShared, NotificationRef.User(grantee.Id), car, actor.DisplayName, before, after,
                ("vehicleName", vehicle.Name), ("userName", grantee.DisplayName)));
        await notifier.NotifyAsync(actor.Id, drafts, ct);
    }

    private async Task<Vehicle> ManageableVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var level = vehicle is null ? AccessLevel.None : await access.VehicleLevelAsync(vehicle, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId });
        if (level < AccessLevel.Edit) throw new ForbiddenException("share.editRequired", "You need edit access to the vehicle to share it.");
        await access.WaitForRunsAsync(vehicle.Id, ct);
        return vehicle;
    }
}
