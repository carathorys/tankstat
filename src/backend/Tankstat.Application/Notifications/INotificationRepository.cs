using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>Every read and write is limited to one recipient; nobody reaches another user's notifications.</summary>
public interface INotificationRepository
{
    /// <summary>Most recently changed first (then by id, so pages are stable).</summary>
    Task<IReadOnlyList<Notification>> ListAsync(Guid recipientId, bool unreadOnly, int skip, int take, CancellationToken ct);
    Task<int> CountAsync(Guid recipientId, bool unreadOnly, CancellationToken ct);

    /// <summary>The recipient's unread notification of the topic about the subject (in the context), which a new event is folded into.</summary>
    Task<Notification?> FindOpenAsync(Guid recipientId, NotificationTopic topic, NotificationRef subject, NotificationRef? context, CancellationToken ct);

    /// <summary>The recipient's notifications of the topic about any of the subjects, read or not (to find derived occurrences that already exist).</summary>
    Task<IReadOnlyList<Notification>> ListForSubjectsAsync(Guid recipientId, NotificationTopic topic, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct);

    /// <summary>How many notifications of the given topics the recipient got after <paramref name="since"/>.</summary>
    Task<int> CountCreatedSinceAsync(Guid recipientId, DateTimeOffset since, IReadOnlyCollection<NotificationTopic> topics, CancellationToken ct);

    /// <summary>Adds it, unless the same notification (same identity) was added in the meantime: then nothing changes and it returns false.</summary>
    Task<bool> AddAsync(Notification notification, CancellationToken ct);
    Task UpdateAsync(Notification notification, CancellationToken ct);
    Task RemoveAsync(Notification notification, CancellationToken ct);

    /// <summary>Marks the given (or, with null, all) unread notifications of the recipient read; returns the ones that changed.</summary>
    Task<IReadOnlyList<Notification>> MarkReadAsync(Guid recipientId, IReadOnlyCollection<Guid>? ids, DateTimeOffset at, CancellationToken ct);

    /// <summary>Deletes the recipient's notifications read before <paramref name="readBefore"/>, except those about <paramref name="keepSubjectIds"/>; returns how many.</summary>
    Task<int> PurgeReadAsync(Guid recipientId, DateTimeOffset readBefore, IReadOnlyCollection<Guid> keepSubjectIds, CancellationToken ct);
}
