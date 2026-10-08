using System.Text.Json;
using Tankstat.Application.Access;
using Tankstat.Application.Sync;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Sync;

namespace Tankstat.Application.Notifications;

/// <summary>
/// Turns the changes the server parked into notifications for the current user, when they look at their notifications (nothing runs in
/// the background): one per parked change of a vehicle whose logs they may see as themselves (owned, granted, or open to everyone; an
/// administrator's right to everything does not count), and of the changes they sent. Once someone applies or discards it, it is no longer
/// worked out, and goes like any read notification.
/// </summary>
public sealed class SyncNotificationSync(
    AccessService access, ISyncChangeRepository ledger, IVehicleRepository vehicles, IUserRepository users, Notifier notifier)
{
    /// <summary>Brings the user's parked-change notifications up to date and returns the parked changes (whose notifications must be kept). Once per request.</summary>
    public Task<IReadOnlyCollection<Guid>> SyncAsync(CancellationToken ct)
    {
        lock (_gate) return _sync ??= RunAsync(ct);
    }

    private readonly Lock _gate = new();
    private Task<IReadOnlyCollection<Guid>>? _sync;

    private async Task<IReadOnlyCollection<Guid>> RunAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var parked = await ledger.ListParkedAsync(await access.PersonalLogScopeAsync(AccessLevel.View, ct), me.Id, ct);
        if (parked.Count == 0) return [];

        var names = (await vehicles.ListByIdsAsync([.. parked.Where(c => c.VehicleId is not null).Select(c => c.VehicleId!.Value).Distinct()], ct)).ToDictionary(v => v.Id, v => v.Name);
        var submitters = (await users.ListByIdsAsync([.. parked.Select(c => c.SubmittedById).Distinct()], ct)).ToDictionary(u => u.Id, u => u.DisplayName);
        var drafts = parked.Select(c => new NotificationDraft(
                me.Id,
                NotificationKind.SyncChangeParked,
                NotificationRef.SyncChange(c.Id),
                c.VehicleId is { } vehicleId ? NotificationRef.Vehicle(vehicleId) : null,
                NotificationArgs.Of(
                    ("change", KindName(c.Kind)),
                    ("vehicleName", c.VehicleId is { } id && names.TryGetValue(id, out var name) ? name : ""),
                    ("submitterName", submitters.GetValueOrDefault(c.SubmittedById, "")),
                    ("reason", c.ReasonKey ?? ""),
                    ("reasonArgs", JsonSerializer.Serialize(c.ReasonArgs))),
                Occurrence: "parked"))
            .ToList();
        await notifier.NotifyAsync(null, drafts, ct);
        return [.. parked.Select(c => c.Id)];
    }

    /// <summary>The kind as the API spells it (LOG_REFUELING), so the app words it like the change itself.</summary>
    private static string KindName(SyncChangeKind kind) =>
        string.Concat(kind.ToString().Select((ch, i) => i > 0 && char.IsUpper(ch) ? $"_{ch}" : ch.ToString())).ToUpperInvariant();
}
