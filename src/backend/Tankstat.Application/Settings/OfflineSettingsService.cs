using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Settings;

namespace Tankstat.Application.Settings;

/// <summary>A user's offline window: the default and the vehicles that differ from it (see <see cref="OfflineWindow"/>).</summary>
public sealed record OfflineSettingsView(string DefaultWindow, IReadOnlyList<OfflineVehicleSetting> Vehicles);

/// <summary>
/// What a user's devices download for offline use. It follows the user to every device, like the UI settings (the anonymous user's when
/// authentication is off, shared by every visitor).
/// </summary>
public sealed class OfflineSettingsService(
    IOfflineSettingsRepository settings, IVehicleRepository vehicles, AccessService access, TimeProvider clock, ILogger<OfflineSettingsService> logger)
{
    public const int MaxVehicles = VehicleQuery.MaxTake;

    public async Task<OfflineSettingsView> GetAsync(CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var row = await settings.FindAsync(user.Id, ct);
        return new OfflineSettingsView(row?.DefaultWindow ?? OfflineWindow.Default, await settings.ListVehiclesAsync(user.Id, ct));
    }

    /// <summary>
    /// Replaces the whole set: the default and the vehicles that differ from it (a vehicle listed twice keeps its first window). Every
    /// vehicle must be one the user may see; an unknown, trashed or invisible one looks non-existent and nothing is saved.
    /// </summary>
    public async Task<OfflineSettingsView> SetAsync(string defaultWindow, IReadOnlyList<(Guid VehicleId, string Window)> vehicleWindows, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var distinct = vehicleWindows.DistinctBy(v => v.VehicleId).ToList();
        if (distinct.Count > MaxVehicles)
            throw new DomainException("settings.offlineTooMany", $"At most {MaxVehicles} vehicles can have a window of their own.", new { Max = MaxVehicles });
        var row = OfflineSettings.Create(user.Id, defaultWindow, clock.GetUtcNow()); // checks the default
        var rows = distinct.Select(v => OfflineVehicleSetting.Create(user.Id, v.VehicleId, v.Window)).ToList(); // checks each window

        var scope = await access.VehicleScopeAsync(AccessLevel.View, ct);
        var ids = rows.Select(r => r.VehicleId).ToList();
        var visible = (await vehicles.ListByIdsAsync(ids, ct)).Where(v => scope.Contains(v.OwnerId, v.Id)).Select(v => v.Id).ToHashSet();
        if (ids.Where(id => !visible.Contains(id)).Cast<Guid?>().FirstOrDefault() is { } unknown)
            throw new NotFoundException("vehicle.notFound", "Vehicle not found.", new { Id = unknown });

        await settings.ReplaceAsync(row, rows, ct);
        logger.LogDebug("User {UserId} set their offline window, {Count} vehicles with their own", user.Id, rows.Count);
        return new OfflineSettingsView(row.DefaultWindow, rows);
    }
}
