using Tankstat.Application.Access;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Vehicles;

public sealed class VehicleService(IVehicleRepository vehicles, AccessService access, TimeProvider clock)
{
    public async Task<IReadOnlyList<Vehicle>> ListAsync(VehicleQuery query, CancellationToken ct) =>
        await vehicles.ListAsync(await access.ScopeAsync(AccessLevel.View, ct), query.Normalized(), ct);

    public async Task<int> CountAsync(CancellationToken ct) =>
        await vehicles.CountAsync(await access.ScopeAsync(AccessLevel.View, ct), ct);

    /// <summary>Null when the vehicle does not exist or the user may not see it (existence is not revealed).</summary>
    public async Task<Vehicle?> FindAsync(Guid id, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(id, ct);
        return vehicle is not null && await access.CanAsync(vehicle.OwnerId, AccessLevel.View, ct) ? vehicle : null;
    }

    public async Task<Vehicle> AddAsync(string name, string? licensePlate, FuelType fuelType, CancellationToken ct)
    {
        var owner = await access.RequirePrincipalAsync(ct);
        var vehicle = Vehicle.Create(owner.Id, name, licensePlate, fuelType);
        await vehicles.AddAsync(vehicle, ct);
        return vehicle;
    }

    public async Task<Vehicle> UpdateAsync(Guid id, string name, string? licensePlate, FuelType fuelType, CancellationToken ct)
    {
        var vehicle = await EditableAsync(id, includeDeleted: false, ct);
        vehicle.Update(name, licensePlate, fuelType);
        await vehicles.UpdateAsync(vehicle, ct);
        return vehicle;
    }

    /// <summary>Moves the vehicle to the trash; it can be restored until it is purged.</summary>
    public async Task<Vehicle> DeleteAsync(Guid id, CancellationToken ct)
    {
        var vehicle = await EditableAsync(id, includeDeleted: false, ct);
        vehicle.MarkDeleted(clock.GetUtcNow());
        await vehicles.UpdateAsync(vehicle, ct);
        return vehicle;
    }

    public async Task<Vehicle> RestoreAsync(Guid id, CancellationToken ct)
    {
        var vehicle = await EditableAsync(id, includeDeleted: true, ct);
        vehicle.Restore();
        await vehicles.UpdateAsync(vehicle, ct);
        return vehicle;
    }

    /// <summary>The trashed vehicles the user could restore (those they may edit).</summary>
    public async Task<IReadOnlyList<Vehicle>> ListTrashAsync(VehicleQuery query, CancellationToken ct) =>
        await vehicles.ListDeletedAsync(await access.ScopeAsync(AccessLevel.Edit, ct), query.Normalized(), ct);

    public async Task<int> CountTrashAsync(CancellationToken ct) =>
        await vehicles.CountDeletedAsync(await access.ScopeAsync(AccessLevel.Edit, ct), ct);

    /// <summary>Permanently removes everything in the trash that the user may edit. Returns how many vehicles were removed.</summary>
    public async Task<int> EmptyTrashAsync(CancellationToken ct) =>
        await vehicles.PurgeAsync(await access.ScopeAsync(AccessLevel.Edit, ct), ct);

    /// <summary>Loads a vehicle for changing; one the user cannot see looks missing, one they can only view is forbidden.</summary>
    private async Task<Vehicle> EditableAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        var vehicle = includeDeleted ? await vehicles.FindIncludingDeletedAsync(id, ct) : await vehicles.FindAsync(id, ct);
        var level = vehicle is null ? AccessLevel.None : await access.LevelAsync(vehicle.OwnerId, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException("vehicle.notFound", $"Vehicle {id} does not exist.", new { Id = id });
        if (level < AccessLevel.Edit) throw new Auth.ForbiddenException("vehicle.viewOnly", "You may only view this vehicle.");
        return vehicle;
    }
}
