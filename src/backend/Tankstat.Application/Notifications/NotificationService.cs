using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>
/// The current user's inbox. Notifications belong to their recipient alone: nobody, administrators included, sees or changes anyone
/// else's. Reading the inbox first brings the derived notifications (recurring expenses) up to date.
/// </summary>
public sealed class NotificationService(AccessService access, INotificationRepository notifications, RecurringNotificationSync recurring, TimeProvider clock)
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    public async Task<IReadOnlyList<Notification>> ListAsync(bool unreadOnly, int skip, int take, CancellationToken ct)
    {
        var me = await SyncedAsync(ct);
        return await notifications.ListAsync(me.Id, unreadOnly, Math.Max(0, skip), Math.Clamp(take, 1, MaxTake), ct);
    }

    public async Task<int> CountAsync(bool unreadOnly, CancellationToken ct)
    {
        var me = await SyncedAsync(ct);
        return await notifications.CountAsync(me.Id, unreadOnly, ct);
    }

    /// <summary>Marks the given notifications (null: all of them) read; returns how many were unread.</summary>
    public async Task<int> MarkReadAsync(IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        return await notifications.MarkReadAsync(me.Id, ids, clock.GetUtcNow(), ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var notification = await notifications.FindAsync(me.Id, id, ct)
            ?? throw new NotFoundException("notification.notFound", $"Notification {id} does not exist.", new { Id = id });
        await notifications.RemoveAsync(notification, ct);
    }

    /// <summary>Deletes the read notifications; returns how many.</summary>
    public async Task<int> DeleteReadAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        return await notifications.DeleteReadAsync(me.Id, ct);
    }

    private async Task<Principal> SyncedAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        await recurring.SyncAsync(ct);
        return me;
    }
}
