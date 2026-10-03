using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>
/// The current user's inbox. Notifications belong to their recipient alone: nobody, administrators included, sees or changes anyone
/// else's. Users only mark them read (when, is kept); the system removes them <see cref="NotificationOptions.ReadRetentionDays"/> after
/// that. Reading the inbox first brings it up to date: the derived notifications (recurring expenses) are worked out and the expired
/// ones removed (there is no background job).
/// </summary>
public sealed class NotificationService(
    AccessService access, INotificationRepository notifications, RecurringNotificationSync recurring, IOptions<NotificationOptions> options, TimeProvider clock,
    ILogger<NotificationService> logger)
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

    /// <summary>Marks the given notifications (null: all of them) read now; returns the ones that were unread.</summary>
    public async Task<IReadOnlyList<Notification>> MarkReadAsync(IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        return await notifications.MarkReadAsync(me.Id, ids, clock.GetUtcNow(), ct);
    }

    private async Task<Principal> SyncedAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var due = await recurring.SyncAsync(ct);
        // A reminder of a schedule that is still due is kept, or the next look would add it again as new.
        var purged = await notifications.PurgeReadAsync(me.Id, clock.GetUtcNow().AddDays(-options.Value.ReadRetentionDays), due, ct);
        if (purged > 0) logger.LogDebug("Purged {Count} read notifications of user {UserId}", purged, me.Id);
        return me;
    }
}
