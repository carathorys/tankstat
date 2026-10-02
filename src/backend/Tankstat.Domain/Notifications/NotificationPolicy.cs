namespace Tankstat.Domain.Notifications;

/// <summary>
/// Something a source wants to tell a user. With an <see cref="Occurrence"/> it is <i>derived</i>: worked out again and again from the
/// current state (a schedule being due), so the same occurrence must never be stored twice. Without one it is an <i>event</i>
/// (someone changed an access level), with the values <see cref="Before"/> and <see cref="After"/> the change, when it has them.
/// </summary>
public sealed record NotificationDraft(
    Guid RecipientId, NotificationKind Kind, NotificationRef Subject, NotificationRef? Context, IReadOnlyDictionary<string, string> Args,
    string? Occurrence = null, string? Before = null, string? After = null)
{
    public bool IsDerived => Occurrence is not null;
}

/// <summary>What has to happen to the stored notifications for a draft.</summary>
public abstract record NotificationChange
{
    private NotificationChange() { }

    public sealed record Add(Notification Notification) : NotificationChange;
    public sealed record Update(Notification Notification) : NotificationChange;
    public sealed record Remove(Notification Notification) : NotificationChange;
    public sealed record Nothing : NotificationChange;
}

/// <summary>
/// Keeps users from being flooded: decides how a draft meets what the recipient already has.
/// <list type="bullet">
/// <item>Derived: a new occurrence is added once; the same occurrence is only ever raised to a more urgent kind (which makes it unread again).</item>
/// <item>Events are folded into the recipient's unread notification about the same thing, and a change that is undone before it was read
/// removes that notification.</item>
/// <item>Past <c>maxPerHour</c> new events in the last hour, further events only count up a single "more activity" notification.
/// Derived notifications do not count against the limit: they exist at most once per occurrence anyway.</item>
/// </list>
/// </summary>
public static class NotificationPolicy
{
    /// <param name="existing">Derived: the stored notification of the same occurrence (read or not). Event: the recipient's unread one about the same thing.</param>
    /// <param name="createdLastHour">Event only: how many notifications the recipient got in the last hour.</param>
    /// <param name="openDigest">Event only: the recipient's unread "more activity" notification.</param>
    public static NotificationChange Decide(
        NotificationDraft draft, Notification? existing, int createdLastHour, Notification? openDigest, int maxPerHour, DateTimeOffset now)
    {
        if (draft.IsDerived)
        {
            if (existing is null) return new NotificationChange.Add(Create(draft, draft.Occurrence!, now));
            if (NotificationKinds.RankOf(draft.Kind) <= NotificationKinds.RankOf(existing.Kind)) return new NotificationChange.Nothing();
            existing.Raise(draft.Kind, draft.Args, now);
            return new NotificationChange.Update(existing);
        }

        if (existing is not null)
        {
            if (draft.After is not null && draft.After == existing.Before) return new NotificationChange.Remove(existing); // back where it started
            existing.Fold(draft.Kind, draft.Args, now);
            return new NotificationChange.Update(existing);
        }

        if (createdLastHour >= maxPerHour)
        {
            if (openDigest is null) return new NotificationChange.Add(Notification.Create(
                draft.RecipientId, NotificationKind.MoreActivity, NotificationRef.Instance, null, NewOccurrence(), new Dictionary<string, string>(), null, now));
            openDigest.Fold(NotificationKind.MoreActivity, openDigest.Args, now);
            return new NotificationChange.Update(openDigest);
        }

        return new NotificationChange.Add(Create(draft, NewOccurrence(), now));
    }

    private static Notification Create(NotificationDraft draft, string occurrence, DateTimeOffset now) =>
        Notification.Create(draft.RecipientId, draft.Kind, draft.Subject, draft.Context, occurrence, draft.Args, draft.Before, now);

    private static string NewOccurrence() => Guid.NewGuid().ToString("N");
}
