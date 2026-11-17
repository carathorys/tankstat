using System.Globalization;
using Tankstat.Application.Notifications;
using Tankstat.Application.Users;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Exports;

/// <summary>
/// Who is told how an export ended (#341): the person, with no actor (the notifier leaves an actor out of the recipients), and for a
/// finished one every administrator (the owner's decision 3), with the exporter as actor, so an exporting administrator is not told of
/// their own. With sign-in off nobody is an administrator and there is nobody to tell: the log line stands.
/// </summary>
public sealed class ExportNotifier(IUserRepository users, Notifier notifier)
{
    public async Task ReadyAsync(ExportRecord record, CancellationToken ct)
    {
        if (record.OwnerId == Guid.Empty) return;
        var subject = NotificationRef.Export(record.Id);
        var args = NotificationArgs.Of(
            ("count", record.CarNames.Count.ToString(CultureInfo.InvariantCulture)),
            ("bytes", (record.Bytes ?? 0).ToString(CultureInfo.InvariantCulture)),
            ("keptUntil", record.KeptUntil?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        await notifier.NotifyAsync(null, [new NotificationDraft(record.OwnerId, NotificationKind.ExportReady, subject, null, args)], ct);
        var actor = (await users.FindByIdAsync(record.OwnerId, ct))?.DisplayName;
        var administrators = (await users.ListAsync(ct)).Where(u => u.IsAdmin && !u.IsDisabled).Select(u => u.Id);
        var told = NotificationArgs.Of(("actor", actor), ("count", record.CarNames.Count.ToString(CultureInfo.InvariantCulture)), ("names", Names(record.CarNames)));
        await notifier.NotifyAsync(record.OwnerId, administrators.Select(id => new NotificationDraft(id, NotificationKind.CarsExported, subject, NotificationRef.User(record.OwnerId), told)), ct);
    }

    public async Task FailedAsync(ExportRecord record, CancellationToken ct)
    {
        if (record.OwnerId == Guid.Empty) return;
        await notifier.NotifyAsync(null, [new NotificationDraft(record.OwnerId, NotificationKind.ExportFailed, NotificationRef.Export(record.Id), null,
            NotificationArgs.Of(("reason", record.FailureKey)))], ct);
    }

    /// <summary>A few names, then how many more: a notification is a line, not a list.</summary>
    private static string Names(IReadOnlyList<string> names) =>
        string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" +{names.Count - 5}" : "");
}
