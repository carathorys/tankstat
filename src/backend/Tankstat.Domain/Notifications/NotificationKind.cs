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

    /// <summary>To whoever logged it: values of a log saved while its photo was being read were filled in from the photo; they should check them.</summary>
    LogFilledFromPhoto,

    /// <summary>To whoever logged it: the photos of a log saved with empty values were read, but values are still missing.</summary>
    LogNotFilled,

    /// <summary>A change sent from a device that the server could not apply waits for someone to apply or discard it (the recipient sent it or may see the vehicle).</summary>

    SyncChangeParked,


    /// <summary>More happened than the recipient should be told one by one (the hourly limit was reached); the count says how much.</summary>
    MoreActivity,

    /// <summary>To every administrator (the anonymous user with sign-in off): a backup the app was to write failed; one a day at most.</summary>
    BackupFailed,

    /// <summary>A tyre set in storage on a vehicle the recipient can see has reached the month it is to be fitted from (#292); one per set and season.</summary>
    TyreSwapDue,

    /// <summary>To the person who started it: their import from a backup finished (#337); the report is kept for a week.</summary>
    ImportFinished,

    /// <summary>To the person who started it: their import brought nothing in (it failed, took too long, never got its turn, or the server restarted).</summary>
    ImportNotFinished,

    /// <summary>To the person who started it: someone else (an administrator) cancelled their import.</summary>
    ImportCancelled,

    /// <summary>To every administrator: a user imported cars from a backup.</summary>
    CarsImported,

    /// <summary>To the administrator who started it: their partial restore finished (#340); the report is kept for a week.</summary>
    RestoreFinished,

    /// <summary>To the administrator who started it: their restore brought nothing back.</summary>
    RestoreNotFinished,

    /// <summary>To the administrator who started it: another administrator cancelled their restore.</summary>
    RestoreCancelled,

    /// <summary>To the other administrators: an administrator restored cars from a backup.</summary>
    CarsRestored,

    /// <summary>To a restored car's owner and everyone it is shared with: what came back (#340, one per car).</summary>
    CarRestored,

    /// <summary>To the person who started it: their export is ready to download (#341), kept for <c>Exports:KeepHours</c>.</summary>
    ExportReady,

    /// <summary>To the person who started it: their export could not be built; nothing was kept.</summary>
    ExportFailed,

    /// <summary>To every administrator: a user exported cars (the owner's decision 3 on #341).</summary>
    CarsExported,
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
    LogReview,
    Sync,
    Digest,
    Backup,
    Tyres,
    Imports,
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
        NotificationKind.LogFilledFromPhoto or NotificationKind.LogNotFilled => NotificationTopic.LogReview,
        NotificationKind.SyncChangeParked => NotificationTopic.Sync,
        NotificationKind.MoreActivity => NotificationTopic.Digest,
        NotificationKind.BackupFailed => NotificationTopic.Backup,
        NotificationKind.TyreSwapDue => NotificationTopic.Tyres,
        NotificationKind.ImportFinished or NotificationKind.ImportNotFinished or NotificationKind.ImportCancelled or NotificationKind.CarsImported
            or NotificationKind.RestoreFinished or NotificationKind.RestoreNotFinished or NotificationKind.RestoreCancelled or NotificationKind.CarsRestored
            or NotificationKind.CarRestored or NotificationKind.ExportReady or NotificationKind.ExportFailed or NotificationKind.CarsExported => NotificationTopic.Imports,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Topics whose notifications are derived (worked out on read from the current state) rather than sent by events.</summary>
    public static IReadOnlySet<NotificationTopic> DerivedTopics { get; } = new HashSet<NotificationTopic> { NotificationTopic.Recurring, NotificationTopic.LogReview, NotificationTopic.Sync, NotificationTopic.Backup, NotificationTopic.Tyres };

    /// <summary>Topics whose notifications are sent by events; only these count against the hourly limit.</summary>
    public static IReadOnlyList<NotificationTopic> EventTopics { get; } = Enum.GetValues<NotificationTopic>().Where(t => !DerivedTopics.Contains(t)).ToList();

    /// <summary>How urgent a kind is within its topic: a derived notification is only raised to a more urgent kind, never lowered.</summary>
    public static int RankOf(NotificationKind kind) => kind is NotificationKind.RecurringOverdue or NotificationKind.LogNotFilled ? 1 : 0;
}
