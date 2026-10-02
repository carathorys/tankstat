using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>Builds the display-ready arguments of a notification (the frontend fills its texts with them).</summary>
internal static class NotificationArgs
{
    public static IReadOnlyDictionary<string, string> Of(params (string Name, string? Value)[] args) => args
        .Where(a => a.Value is not null)
        .ToDictionary(a => a.Name, a => a.Value!.Length > Notification.MaxArgValueLength ? a.Value[..Notification.MaxArgValueLength] : a.Value!);

    /// <summary>
    /// A notification that an access level changed from <paramref name="before"/> to <paramref name="after"/>: it names who did it and the
    /// new level (spelled like the API, e.g. <c>EDIT</c>, so clients reuse their translations), and the two levels let a change that is
    /// undone before it was read disappear.
    /// </summary>
    public static NotificationDraft AccessChange(
        Guid recipientId, NotificationKind kind, NotificationRef subject, NotificationRef? context, string actorName, AccessLevel before, AccessLevel after,
        params (string Name, string? Value)[] args)
    {
        var (from, to) = (Level(before), Level(after));
        return new NotificationDraft(recipientId, kind, subject, context, Of([.. args, ("actorName", actorName), ("level", to)]), Before: from, After: to);
    }

    private static string Level(AccessLevel level) => level.ToString().ToUpperInvariant();
}
