using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Notifications;
using Tankstat.Application.Users;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Imports;

/// <summary>
/// Who is told how an import from a backup ended (#337), always after the fact: the person who started it, and for a finished one every
/// administrator (with sign-in off the anonymous user is both, and is told once). The person's own are sent with no actor, since the
/// notifier leaves an actor out of the recipients; the administrators' has the importer as actor, so an importing administrator is not
/// told of their own import. The arguments are counts, the cars' names (the importer's own cars now) and the reason.
/// </summary>
public sealed class ImportRunNotifier(IUserRepository users, Notifier notifier, IOptions<AuthOptions> auth)
{
    public const int MaxNames = 5;

    public async Task FinishedAsync(ImportRun run, ImportRunReport report, CancellationToken ct)
    {
        var imported = report.Cars.Where(c => !c.LeftOut).ToList();
        var args = NotificationArgs.Of(
            ("count", imported.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("names", Names(imported.Select(c => c.TargetName ?? c.Name))),
            ("refused", report.Refused.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var subject = NotificationRef.ImportRun(run.Id);
        await notifier.NotifyAsync(null, [new NotificationDraft(run.StartedById, NotificationKind.ImportFinished, subject, null, args)], ct);
        if (auth.Value.Mode == AuthMode.None || imported.Count == 0) return;
        var administrators = (await users.ListAsync(ct)).Where(u => u.IsAdmin && !u.IsDisabled).Select(u => u.Id);
        var told = NotificationArgs.Of([.. args.Select(a => (a.Key, (string?)a.Value)), ("actor", await NameAsync(run.StartedById, ct))]);
        await notifier.NotifyAsync(run.StartedById, administrators.Select(id => new NotificationDraft(id, NotificationKind.CarsImported, subject, NotificationRef.User(run.StartedById), told)), ct);
    }

    /// <summary>Nothing came in. Cancelled by its starter: they know. Cancelled by someone else: said so, with who.</summary>
    public async Task NotFinishedAsync(ImportRun run, ImportRunFailure failure, Guid? cancelledById, CancellationToken ct)
    {
        var subject = NotificationRef.ImportRun(run.Id);
        if (failure == ImportRunFailure.Cancelled)
        {
            if (cancelledById is null || cancelledById == run.StartedById) return;
            await notifier.NotifyAsync(cancelledById, [new NotificationDraft(run.StartedById, NotificationKind.ImportCancelled, subject, NotificationRef.User(cancelledById.Value), NotificationArgs.Of(("actor", await NameAsync(cancelledById.Value, ct))))], ct);
            return;
        }
        await notifier.NotifyAsync(null, [new NotificationDraft(run.StartedById, NotificationKind.ImportNotFinished, subject, null, NotificationArgs.Of(("reason", failure.ToString())))], ct);
    }

    private async Task<string?> NameAsync(Guid userId, CancellationToken ct) => (await users.FindByIdAsync(userId, ct))?.DisplayName;

    /// <summary>A few names, then how many more: a notification is a line, not a list.</summary>
    private static string Names(IEnumerable<string> names)
    {
        var all = names.ToList();
        return string.Join(", ", all.Take(MaxNames)) + (all.Count > MaxNames ? $" +{all.Count - MaxNames}" : "");
    }
}
