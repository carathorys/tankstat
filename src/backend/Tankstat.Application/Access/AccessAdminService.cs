using Tankstat.Application.Auth;
using Tankstat.Application.Notifications;
using Tankstat.Application.Users;
using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Access;

/// <summary>Administrator-only management of the instance-wide default and per-user grants; the users whose access changed are notified.</summary>
public sealed class AccessAdminService(
    AccessService access, IAccessSettingsRepository settings, IAccessGrantRepository grants, IUserRepository users, Notifier notifier)
{
    public async Task<AccessSettings> GetSettingsAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await settings.GetAsync(ct);
    }

    public async Task<AccessSettings> SetDefaultLevelAsync(AccessLevel level, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var current = await settings.GetAsync(ct);
        var before = current.DefaultLevelForOthers;
        current.SetDefaultLevelForOthers(level);
        await settings.SaveAsync(current, ct);

        if (before != level)
        {
            // Administrators can access everything anyway, so only the others are affected: one notification each, nothing per vehicle.
            var affected = (await users.ListAsync(ct)).Where(u => !u.IsAdmin && !u.IsDisabled);
            await notifier.NotifyAsync(admin.Id, affected.Select(u =>
                NotificationArgs.AccessChange(u.Id, NotificationKind.DefaultAccessChanged, NotificationRef.Instance, null, admin.DisplayName, before, level)), ct);
        }
        return current;
    }

    public async Task<IReadOnlyList<AccessGrant>> ListGrantsAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await grants.ListAsync(ct);
    }

    /// <summary>Gives <paramref name="granteeId"/> the level on <paramref name="ownerId"/>'s data; <see cref="AccessLevel.None"/> removes the grant.</summary>
    public async Task SetGrantAsync(Guid ownerId, Guid granteeId, AccessLevel level, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var owner = await users.FindByIdAsync(ownerId, ct) ?? throw new NotFoundException("user.notFound", $"User {ownerId} does not exist.", new { Id = ownerId });
        var grantee = await users.FindByIdAsync(granteeId, ct) ?? throw new NotFoundException("user.notFound", $"User {granteeId} does not exist.", new { Id = granteeId });

        var existing = await grants.FindAsync(ownerId, granteeId, ct);
        var before = existing?.Level ?? AccessLevel.None;
        if (level == AccessLevel.None)
        {
            if (existing is not null) await grants.RemoveAsync(existing, ct);
        }
        else if (existing is null)
        {
            await grants.AddAsync(AccessGrant.Create(ownerId, granteeId, level), ct);
        }
        else
        {
            existing.ChangeLevel(level);
            await grants.UpdateAsync(existing, ct);
        }

        if (before != level) await NotifyGrantAsync(admin, owner, grantee, before, level, ct);
    }

    private Task NotifyGrantAsync(Principal admin, User owner, User grantee, AccessLevel before, AccessLevel after, CancellationToken ct) =>
        notifier.NotifyAsync(admin.Id,
        [
            NotificationArgs.AccessChange(grantee.Id, NotificationKind.DataAccessChanged, NotificationRef.User(owner.Id), null, admin.DisplayName, before, after, ("userName", owner.DisplayName)),
            NotificationArgs.AccessChange(owner.Id, NotificationKind.DataShared, NotificationRef.User(grantee.Id), null, admin.DisplayName, before, after, ("userName", grantee.DisplayName)),
        ], ct);
}
