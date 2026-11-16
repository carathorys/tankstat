using System.Globalization;
using Tankstat.Application.Access;
using Tankstat.Application.Tyres;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;

namespace Tankstat.Application.Notifications;

/// <summary>
/// Turns the tyre sets whose swap reminder is due (#292) into notifications for the current user, when they look at their notifications
/// (nothing runs in the background), like <see cref="RecurringNotificationSync"/>: the sets of every vehicle whose logs the user may see as
/// themselves. A set's season (the 1st of its month, as <c>yyyy-MM</c>) is one notification, so working it out again changes nothing, and
/// the next season raises a new one. The swap, or <em>Not this season</em>, removes nothing: the notification is history.
/// </summary>
public sealed class TyreReminderSync(AccessService access, ITyreRepository tyres, IVehicleRepository vehicles, TyreService service, Notifier notifier)
{
    /// <summary>
    /// Brings the user's swap notifications up to date and returns the sets that are due (whose notifications must be kept).
    /// Runs once per request (scope), however often it is asked.
    /// </summary>
    public Task<IReadOnlyCollection<Guid>> SyncAsync(CancellationToken ct)
    {
        lock (_gate) return _sync ??= RunAsync(ct);
    }

    private readonly Lock _gate = new();
    private Task<IReadOnlyCollection<Guid>>? _sync;

    private async Task<IReadOnlyCollection<Guid>> RunAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var scope = await access.PersonalLogScopeAsync(AccessLevel.View, ct);
        var vehicleIds = await tyres.ListReminderVehicleIdsAsync(scope, ct);
        if (vehicleIds.Count == 0) return [];

        var visible = await vehicles.ListByIdsAsync(vehicleIds, ct);
        var names = visible.ToDictionary(v => v.Id, v => v.Name);
        var drafts = (await service.ListRemindersAsync(visible, ct)).Values.SelectMany(due => due).Select(r => new NotificationDraft(
                me.Id,
                NotificationKind.TyreSwapDue,
                NotificationRef.TyreSet(r.SetId),
                NotificationRef.Vehicle(r.VehicleId),
                NotificationArgs.Of(("set", r.Name), ("season", SeasonName(r.Season)), ("vehicleName", names[r.VehicleId])),
                Occurrence: r.SeasonStart.ToString("yyyy-MM", CultureInfo.InvariantCulture)))
            .ToList();
        await notifier.NotifyAsync(null, drafts, ct);
        return [.. drafts.Select(d => d.Subject.Id)];
    }

    /// <summary>The set's season in the API's spelling, which the frontend words.</summary>
    private static string SeasonName(Domain.Tyres.TyreSeason season) => season switch
    {
        Domain.Tyres.TyreSeason.Winter => "WINTER",
        Domain.Tyres.TyreSeason.Summer => "SUMMER",
        _ => "ALL_SEASON",
    };
}
