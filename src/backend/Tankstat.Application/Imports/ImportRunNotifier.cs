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
public sealed class ImportRunNotifier(IUserRepository users, Notifier notifier, IOptions<AuthOptions> auth, Access.IResourceGrantRepository grants)
{
    public const int MaxNames = 5;

    public async Task FinishedAsync(ImportRun run, ImportRunReport report, CancellationToken ct)
    {
        if (run.Kind == ImportRunKind.Restore)
        {
            await RestoredAsync(run, report, ct);
            return;
        }
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
        var restore = run.Kind == ImportRunKind.Restore;
        if (failure == ImportRunFailure.Cancelled)
        {
            if (cancelledById is null || cancelledById == run.StartedById) return;
            await notifier.NotifyAsync(cancelledById, [new NotificationDraft(run.StartedById, restore ? NotificationKind.RestoreCancelled : NotificationKind.ImportCancelled, subject,
                NotificationRef.User(cancelledById.Value), NotificationArgs.Of(("actor", await NameAsync(cancelledById.Value, ct))))], ct);
            return;
        }
        await notifier.NotifyAsync(null, [new NotificationDraft(run.StartedById, restore ? NotificationKind.RestoreNotFinished : NotificationKind.ImportNotFinished, subject, null,
            NotificationArgs.Of(("reason", failure.ToString())))], ct);
    }

    /// <summary>
    /// A partial restore that committed (#340): one notification per car to its owner and everyone its logs are shared with now (an owner of
    /// many cars gets one per car up to the hourly limit, the rest folded into the digest the notifier makes), one to the administrator
    /// who ran it (no actor, as an import's), and one to the other administrators. The administrator is the actor of the rest, so never told
    /// of their own restore twice.
    /// </summary>
    private async Task RestoredAsync(ImportRun run, ImportRunReport report, CancellationToken ct)
    {
        var plan = ImportRunJson.Read<ImportRunPlan>(run.Plan);
        var date = plan?.BackupOf is { } of ? of.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "";
        var restored = report.Cars.Where(c => !c.LeftOut).ToList();
        var actor = await NameAsync(run.StartedById, ct);
        var subject = NotificationRef.ImportRun(run.Id);
        var count = restored.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var names = Names(restored.Select(c => c.TargetName ?? c.Name));
        await notifier.NotifyAsync(null, [new NotificationDraft(run.StartedById, NotificationKind.RestoreFinished, subject, null, NotificationArgs.Of(
            ("count", count), ("names", names), ("refused", report.Refused.ToString(System.Globalization.CultureInfo.InvariantCulture))))], ct);

        var drafts = new List<NotificationDraft>();
        foreach (var car in restored.Where(c => c.VehicleId is not null && c.Restore is not null))
        {
            var replaced = (car.Kinds ?? []).Where(k => k.Mode == ImportKindMode.ReplaceAll).ToList();
            // The entries that came back, and those that took the backup's values again; and whether anything else came (a set without
            // lines, a chart, the picture, a dashboard), which is said without a count (the owner's answer on #376).
            var back = car.Refuelings + car.Expenses + car.Schedules + (car.Tyres?.Lines ?? 0) + (car.Tyres?.NoSetLines ?? 0) + (car.Media?.Photos ?? 0)
                + (car.Kinds ?? []).Sum(k => k.Changed);
            var more = car.Tyres?.Sets.Count > 0 || car.Media is { } m && (m.Picture || m.Charts > 0 || m.Dashboard || m.OwnDashboard);
            var vehicle = NotificationRef.Vehicle(car.VehicleId!.Value);
            var recipients = (await grants.ListForResourceAsync(Domain.Access.ResourceType.Vehicle, car.VehicleId.Value, ct))
                .Where(g => g.Feature == Domain.Access.GrantedFeature.Logs).Select(g => g.GranteeId).Prepend(car.Restore!.OwnerId).Distinct();
            foreach (var recipient in recipients)
            {
                var (how, number) = car.Restore.CameBack && recipient == car.Restore.OwnerId ? ("again", back)
                    : replaced.Count > 0 ? ("replaced", replaced.Sum(k => k.Imported)) : back > 0 ? ("back", back) : ("changed", 0);
                // A car still here that got nothing back tells nobody (the owner's answer on #340); a gone one always tells.
                if (!car.Restore.CameBack && back == 0 && replaced.Count == 0 && !more) continue;
                drafts.Add(new NotificationDraft(recipient, NotificationKind.CarRestored, vehicle, NotificationRef.User(run.StartedById), NotificationArgs.Of(
                    ("actor", actor), ("vehicleName", car.TargetName ?? car.Name), ("date", date), ("how", how),
                    ("count", number.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("kinds", string.Join(",", replaced.Select(k => k.Kind))))));
            }
        }
        if (drafts.Count > 0) await notifier.NotifyAsync(run.StartedById, drafts, ct);
        if (auth.Value.Mode == AuthMode.None || restored.Count == 0) return;
        var administrators = (await users.ListAsync(ct)).Where(u => u.IsAdmin && !u.IsDisabled).Select(u => u.Id);
        await notifier.NotifyAsync(run.StartedById, administrators.Select(id => new NotificationDraft(id, NotificationKind.CarsRestored, subject, NotificationRef.User(run.StartedById),
            NotificationArgs.Of(("actor", actor), ("count", count), ("names", names), ("date", date)))), ct);
    }

    private async Task<string?> NameAsync(Guid userId, CancellationToken ct) => (await users.FindByIdAsync(userId, ct))?.DisplayName;

    /// <summary>A few names, then how many more: a notification is a line, not a list.</summary>
    private static string Names(IEnumerable<string> names)
    {
        var all = names.ToList();
        return string.Join(", ", all.Take(MaxNames)) + (all.Count > MaxNames ? $" +{all.Count - MaxNames}" : "");
    }
}
