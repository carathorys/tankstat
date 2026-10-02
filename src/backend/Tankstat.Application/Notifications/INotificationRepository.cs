using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>Every read and write is limited to one recipient; nobody reaches another user's notifications.</summary>
public interface INotificationRepository
{
    /// <summary>Most recently changed first (then by id, so pages are stable).</summary>
    Task<IReadOnlyList<Notification>> ListAsync(Guid recipientId, bool unreadOnly, int skip, int take, CancellationToken ct);
    Task<int> CountAsync(Guid recipientId, bool unreadOnly, CancellationToken ct);
    Task<Notification?> FindAsync(Guid recipientId, Guid id, CancellationToken ct);

    /// <summary>The recipient's unread notification of the topic about the subject (in the context), which a new event is folded into.</summary>
    Task<Notification?> FindOpenAsync(Guid recipientId, NotificationTopic topic, NotificationRef subject, NotificationRef? context, CancellationToken ct);

    /// <summary>The recipient's notifications of the topic about any of the subjects, read or not (to find derived occurrences that already exist).</summary>
    Task<IReadOnlyList<Notification>> ListForSubjectsAsync(Guid recipientId, NotificationTopic topic, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct);

    Task<int> CountCreatedSinceAsync(Guid recipientId, DateTimeOffset since, CancellationToken ct);

    /// <summary>Adds it, unless the same notification (same identity) was added in the meantime: then nothing changes and it returns false.</summary>
    Task<bool> AddAsync(Notification notification, CancellationToken ct);
    Task UpdateAsync(Notification notification, CancellationToken ct);
    Task RemoveAsync(Notification notification, CancellationToken ct);

    /// <summary>Marks the given (or, with null, all) unread notifications of the recipient read; returns how many changed.</summary>
    Task<int> MarkReadAsync(Guid recipientId, IReadOnlyCollection<Guid>? ids, DateTimeOffset at, CancellationToken ct);

    /// <summary>Deletes the recipient's read notifications; returns how many.</summary>
    Task<int> DeleteReadAsync(Guid recipientId, CancellationToken ct);
}
