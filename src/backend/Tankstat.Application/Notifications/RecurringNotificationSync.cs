using System.Globalization;
using Tankstat.Application.Access;
using Tankstat.Application.Recurring;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Recurring;

namespace Tankstat.Application.Notifications;

/// <summary>
/// Turns the recurring expenses that are due soon or overdue into notifications for the current user, when they look at their
/// notifications (nothing runs in the background). It covers the schedules of every vehicle whose logs the user may see as themselves
/// (owned, granted, or open to everyone; an administrator's right to everything does not count). Each cycle of a schedule (from one
/// "done" to the next) is one notification that can only be raised from due soon to overdue, so working it out again changes nothing.
/// </summary>
public sealed class RecurringNotificationSync(
    AccessService access, IRecurringExpenseRepository schedules, IVehicleRepository vehicles, RecurringExpenseService recurring, Notifier notifier)
{
    private readonly Lock _gate = new();
    private Task? _sync;

    /// <summary>Brings the user's recurring notifications up to date, at most once per request (scope), however often it is asked.</summary>
    public Task SyncAsync(CancellationToken ct)
    {
        lock (_gate) return _sync ??= RunAsync(ct);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var scope = await access.PersonalLogScopeAsync(AccessLevel.View, ct);
        var vehicleIds = await schedules.ListVehicleIdsAsync(scope, ct);
        if (vehicleIds.Count == 0) return;

        var visible = await vehicles.ListByIdsAsync(vehicleIds, ct);
        var names = visible.ToDictionary(v => v.Id, v => v.Name);
        var drafts = (await recurring.ListForVehiclesAsync(visible, ct))
            .SelectMany(entry => entry.Value.Where(r => r.Status.State != RecurrenceState.Upcoming).Select(r => new NotificationDraft(
                me.Id,
                r.Status.State == RecurrenceState.Overdue ? NotificationKind.RecurringOverdue : NotificationKind.RecurringDueSoon,
                NotificationRef.RecurringExpense(r.Item.Id),
                NotificationRef.Vehicle(entry.Key),
                NotificationArgs.Of(("title", r.Item.Title), ("vehicleName", names[entry.Key])),
                Occurrence: Cycle(r.Item))))
            .ToList();
        await notifier.NotifyAsync(null, drafts, ct);
    }

    /// <summary>Identifies the schedule's current cycle: marking it done (or changing when it was last done) starts a new one.</summary>
    private static string Cycle(RecurringExpense item) =>
        string.Create(CultureInfo.InvariantCulture, $"{item.LastDoneDate:yyyy-MM-dd}:{item.LastDoneOdometer}");
}
