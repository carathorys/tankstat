using Tankstat.Application.Notifications;
using Tankstat.Domain.Notifications;

namespace Tankstat.Api.GraphQL;

/// <summary>An entity a notification is about or points to.</summary>
public sealed record NotificationRefInfo(NotificationEntityType Type, Guid Id)
{
    public static NotificationRefInfo From(NotificationRef r) => new(r.Type, r.Id);
}

/// <summary>A display-ready value the notification's text is filled with (e.g. <c>vehicleName</c>, <c>level</c>).</summary>
public sealed record NotificationArg(string Name, string Value);

/// <summary>
/// Something the current user was told. <c>count</c> is how many events it stands for (several changes of the same thing are folded into
/// one); <c>updatedAt</c> is when it last changed, which orders the inbox.
/// </summary>
public sealed record NotificationInfo(
    Guid Id, NotificationKind Kind, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Read, int Count,
    NotificationRefInfo Subject, NotificationRefInfo? Context, IReadOnlyList<NotificationArg> Args)
{
    public static NotificationInfo From(Notification n) => new(
        n.Id, n.Kind, n.CreatedAt, n.UpdatedAt, n.IsRead, n.Count, NotificationRefInfo.From(n.Subject),
        n.Context is { } context ? NotificationRefInfo.From(context) : null,
        n.Args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new NotificationArg(a.Key, a.Value)).ToList());
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class NotificationQueries
{
    /// <summary>One page of the current user's notifications, most recently changed first.</summary>
    public async Task<IReadOnlyList<NotificationInfo>> GetNotifications(
        [Service] NotificationService notifications, CancellationToken ct, bool unreadOnly = false, int skip = 0, int take = NotificationService.DefaultTake) =>
        (await notifications.ListAsync(unreadOnly, skip, take, ct)).Select(NotificationInfo.From).ToList();

    /// <summary>How many notifications the current user has (only the unread ones with <c>unreadOnly</c>).</summary>
    public Task<int> GetNotificationCount([Service] NotificationService notifications, CancellationToken ct, bool unreadOnly = false) =>
        notifications.CountAsync(unreadOnly, ct);
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class NotificationMutations
{
    /// <summary>Marks the given notifications read, or all of them when <c>ids</c> is omitted; returns how many were unread.</summary>
    public Task<int> MarkNotificationsRead([Service] NotificationService notifications, CancellationToken ct, IReadOnlyList<Guid>? ids = null) =>
        notifications.MarkReadAsync(ids, ct);

    public async Task<bool> DeleteNotification(Guid id, [Service] NotificationService notifications, CancellationToken ct)
    {
        await notifications.DeleteAsync(id, ct);
        return true;
    }

    /// <summary>Deletes the read notifications; returns how many.</summary>
    public Task<int> DeleteReadNotifications([Service] NotificationService notifications, CancellationToken ct) => notifications.DeleteReadAsync(ct);
}
