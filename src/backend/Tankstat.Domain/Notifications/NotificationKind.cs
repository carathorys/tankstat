namespace Tankstat.Domain.Notifications;

/// <summary>What a notification tells its recipient. The texts live in the frontend (<c>notifications.kinds.*</c>), filled from the arguments.</summary>
public enum NotificationKind
{
    /// <summary>To the grantee: their access to a vehicle's logs was granted, changed or (level None) revoked.</summary>
    LogAccessChanged,

    /// <summary>To a vehicle's owner: someone else changed who may work with the vehicle's logs.</summary>
    VehicleShared,

    /// <summary>To the grantee: an administrator changed their access to everything another user owns.</summary>
    DataAccessChanged,

    /// <summary>To the owner: an administrator changed someone's access to everything they own.</summary>
    DataShared,

    /// <summary>To every user it affects: the instance-wide access to other users' data changed.</summary>
    DefaultAccessChanged,

    /// <summary>A recurring expense the recipient can see is due soon.</summary>
    RecurringDueSoon,

    /// <summary>A recurring expense the recipient can see is overdue.</summary>
    RecurringOverdue,

    /// <summary>More happened than the recipient should be told one by one (the hourly limit was reached); the count says how much.</summary>
    MoreActivity,
}

/// <summary>
/// The thread a notification belongs to. Kinds of one topic replace each other instead of piling up (a due-soon schedule that becomes
/// overdue is the same notification, raised), so what is coalesced and what is unique is decided per topic.
/// </summary>
public enum NotificationTopic
{
    LogAccess,
    VehicleSharing,
    DataAccess,
    DataSharing,
    DefaultAccess,
    Recurring,
    Digest,
}

public static class NotificationKinds
{
    public static NotificationTopic TopicOf(NotificationKind kind) => kind switch
    {
        NotificationKind.LogAccessChanged => NotificationTopic.LogAccess,
        NotificationKind.VehicleShared => NotificationTopic.VehicleSharing,
        NotificationKind.DataAccessChanged => NotificationTopic.DataAccess,
        NotificationKind.DataShared => NotificationTopic.DataSharing,
        NotificationKind.DefaultAccessChanged => NotificationTopic.DefaultAccess,
        NotificationKind.RecurringDueSoon or NotificationKind.RecurringOverdue => NotificationTopic.Recurring,
        NotificationKind.MoreActivity => NotificationTopic.Digest,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>How urgent a kind is within its topic: a derived notification is only raised to a more urgent kind, never lowered.</summary>
    public static int RankOf(NotificationKind kind) => kind == NotificationKind.RecurringOverdue ? 1 : 0;
}
