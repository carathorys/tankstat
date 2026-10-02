using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Notifications;
using Tankstat.Domain.Notifications;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository(IDbContextFactory<AppDbContext> dbFactory) : INotificationRepository
{
    public async Task<IReadOnlyList<Notification>> ListAsync(Guid recipientId, bool unreadOnly, int skip, int take, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Of(db, recipientId, unreadOnly).OrderByDescending(n => n.UpdatedAt).ThenBy(n => n.Id).Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<int> CountAsync(Guid recipientId, bool unreadOnly, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Of(db, recipientId, unreadOnly).CountAsync(ct);
    }

    public async Task<Notification?> FindOpenAsync(Guid recipientId, NotificationTopic topic, NotificationRef subject, NotificationRef? context, CancellationToken ct)
    {
        var contextId = context?.Id ?? Guid.Empty;
        var contextType = context?.Type;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Of(db, recipientId, true)
            .Where(n => n.Topic == topic && n.SubjectType == subject.Type && n.SubjectId == subject.Id && n.ContextType == contextType && n.ContextId == contextId)
            .OrderByDescending(n => n.UpdatedAt).ThenBy(n => n.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Notification>> ListForSubjectsAsync(Guid recipientId, NotificationTopic topic, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct)
    {
        if (subjectIds.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Of(db, recipientId, false).Where(n => n.Topic == topic && subjectIds.Contains(n.SubjectId)).ToListAsync(ct);
    }

    public async Task<int> CountCreatedSinceAsync(Guid recipientId, DateTimeOffset since, IReadOnlyCollection<NotificationTopic> topics, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Of(db, recipientId, false).CountAsync(n => n.CreatedAt > since && topics.Contains(n.Topic), ct);
    }

    public async Task<bool> AddAsync(Notification notification, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            if (await ExistsAsync(notification, ct)) return false; // another request added the same one first: the unique identity kept it single
            throw;
        }
    }

    private async Task<bool> ExistsAsync(Notification n, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.AnyAsync(x =>
            x.RecipientId == n.RecipientId && x.Topic == n.Topic && x.SubjectType == n.SubjectType && x.SubjectId == n.SubjectId &&
            x.ContextId == n.ContextId && x.Occurrence == n.Occurrence, ct);
    }

    public async Task UpdateAsync(Notification notification, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Notifications.Update(notification);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Notification notification, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Notifications.Where(n => n.Id == notification.Id).ExecuteDeleteAsync(ct); // already gone is fine
    }

    public async Task<IReadOnlyList<Notification>> MarkReadAsync(Guid recipientId, IReadOnlyCollection<Guid>? ids, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unread = db.Notifications.Where(n => n.RecipientId == recipientId && n.ReadAt == null);
        if (ids is not null) unread = unread.Where(n => ids.Contains(n.Id));
        var changed = await unread.ToListAsync(ct);
        foreach (var n in changed) n.MarkRead(at);
        await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<int> PurgeReadAsync(Guid recipientId, DateTimeOffset readBefore, IReadOnlyCollection<Guid> keepSubjectIds, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var old = db.Notifications.Where(n => n.RecipientId == recipientId && n.ReadAt != null && n.ReadAt < readBefore);
        if (keepSubjectIds.Count > 0) old = old.Where(n => !keepSubjectIds.Contains(n.SubjectId));
        return await old.ExecuteDeleteAsync(ct);
    }

    private static IQueryable<Notification> Of(AppDbContext db, Guid recipientId, bool unreadOnly)
    {
        var mine = db.Notifications.AsNoTracking().Where(n => n.RecipientId == recipientId);
        return unreadOnly ? mine.Where(n => n.ReadAt == null) : mine;
    }
}
