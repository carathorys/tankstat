using Microsoft.Extensions.Options;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>
/// The one way to tell users something: every source hands its drafts here, and <see cref="NotificationPolicy"/> decides what is stored,
/// so derived notifications stay single and events do not flood anyone. Nobody is notified about what they did themselves.
/// </summary>
public sealed class Notifier(INotificationRepository notifications, IOptions<NotificationOptions> options, TimeProvider clock)
{
    /// <param name="actorId">Who caused it (left out of the recipients); null when nobody did, e.g. a schedule became due.</param>
    public async Task NotifyAsync(Guid? actorId, IEnumerable<NotificationDraft> drafts, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var wanted = drafts.Where(d => d.RecipientId != actorId).ToList();

        // Derived drafts: the occurrences already stored are loaded once per recipient and topic.
        foreach (var group in wanted.Where(d => d.IsDerived).GroupBy(d => (d.RecipientId, Topic: NotificationKinds.TopicOf(d.Kind))))
        {
            var stored = await notifications.ListForSubjectsAsync(group.Key.RecipientId, group.Key.Topic, [.. group.Select(d => d.Subject.Id).Distinct()], ct);
            foreach (var draft in group)
            {
                var existing = stored.FirstOrDefault(n => n.Subject == draft.Subject && n.Context == draft.Context && n.Occurrence == draft.Occurrence);
                await ApplyAsync(NotificationPolicy.Decide(draft, existing, 0, null, options.Value.MaxPerHour, now), ct);
            }
        }

        foreach (var draft in wanted.Where(d => !d.IsDerived))
        {
            var open = await notifications.FindOpenAsync(draft.RecipientId, NotificationKinds.TopicOf(draft.Kind), draft.Subject, draft.Context, ct);
            var createdLastHour = 0;
            Notification? digest = null;
            if (open is null)
            {
                createdLastHour = await notifications.CountCreatedSinceAsync(draft.RecipientId, now.AddHours(-1), NotificationKinds.EventTopics, ct);
                if (createdLastHour >= options.Value.MaxPerHour)
                    digest = await notifications.FindOpenAsync(draft.RecipientId, NotificationTopic.Digest, NotificationRef.Instance, null, ct);
            }
            await ApplyAsync(NotificationPolicy.Decide(draft, open, createdLastHour, digest, options.Value.MaxPerHour, now), ct);
        }
    }

    private Task ApplyAsync(NotificationChange change, CancellationToken ct) => change switch
    {
        NotificationChange.Add add => notifications.AddAsync(add.Notification, ct),
        NotificationChange.Update update => notifications.UpdateAsync(update.Notification, ct),
        NotificationChange.Remove remove => notifications.RemoveAsync(remove.Notification, ct),
        _ => Task.CompletedTask,
    };
}
