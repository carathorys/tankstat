using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Settings;

namespace Tankstat.Application.Settings;

/// <summary>The order of the vehicles on a user's home page: their own arrangement comes first, everything else follows by name.</summary>
public sealed class VehicleOrderService(IVehicleOrderRepository orders, IVehicleRepository vehicles, AccessService access, ILogger<VehicleOrderService> logger)
{
    public const int MaxVehicles = VehicleQuery.MaxTake;

    /// <summary>
    /// Replaces the user's whole arrangement: duplicates are dropped (the first wins), positions run from 0, and every id must be a vehicle
    /// the user may see; an unknown, trashed or invisible one looks non-existent and nothing is saved.
    /// </summary>
    public async Task SetAsync(IReadOnlyList<Guid> vehicleIds, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var ids = vehicleIds.Distinct().ToList();
        if (ids.Count > MaxVehicles)
            throw new DomainException("vehicleOrder.tooMany", $"At most {MaxVehicles} vehicles can be arranged.", new { Max = MaxVehicles });

        var scope = await access.VehicleScopeAsync(AccessLevel.View, ct); // exactly what the home list shows
        var visible = (await vehicles.ListByIdsAsync(ids, ct)).Where(v => scope.Contains(v.OwnerId, v.Id)).Select(v => v.Id).ToHashSet();
        if (ids.Where(id => !visible.Contains(id)).Cast<Guid?>().FirstOrDefault() is { } unknown)
            throw new NotFoundException("vehicle.notFound", "Vehicle not found.", new { Id = unknown });

        await orders.ReplaceAsync(user.Id, ids.Select((id, position) => VehicleOrder.Create(user.Id, id, position)).ToList(), ct);
        logger.LogDebug("User {UserId} arranged {Count} vehicles", user.Id, ids.Count);
    }
}
