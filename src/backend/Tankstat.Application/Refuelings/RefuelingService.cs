using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Odometers;
using Tankstat.Application.Photos;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Refuelings;

/// <summary>What a new log can start from: the vehicle's latest odometer reading and the currency of its latest log.</summary>
public sealed record RefuelingDefaults(long? LastOdometer, DateOnly? LastDate, string? LastCurrency);

/// <param name="Currency">Required when logging; when updating, omit it to keep the log's currency.</param>
public sealed record RefuelingInput(DateOnly Date, decimal Volume, decimal TotalCost, string? Currency, long Odometer, bool IsFullTank, string? Note);

/// <summary>
/// Refuelling logs of a vehicle. Access is the one defined for a vehicle's logs: the owner-based access (owner,
/// administrators, owner-wide grants, instance default) or a grant on that vehicle's logs, whichever is more. Edit may
/// add, change, trash and restore; only Delete may delete permanently.
/// </summary>
public sealed class RefuelingService(
    IVehicleRepository vehicles, IRefuelingRepository refuelings, AccessService access, OdometerService odometer, LogPhotoService photos, TimeProvider clock)
{
    /// <summary>Logs may be dated today in any time zone, but not further ahead.</summary>
    private DateOnly LatestAllowedDate => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);

    public async Task<IReadOnlyList<Refueling>> ListAsync(Guid vehicleId, RefuelingQuery query, CancellationToken ct) =>
        await VisibleVehicleAsync(vehicleId, ct) is null ? [] : await refuelings.ListForVehicleAsync(vehicleId, query.Normalized(), ct);

    public async Task<int> CountAsync(Guid vehicleId, CancellationToken ct) =>
        await VisibleVehicleAsync(vehicleId, ct) is null ? 0 : await refuelings.CountForVehicleAsync(vehicleId, ct);

    /// <summary>Suggestions for the add-log form; null when the vehicle is not visible.</summary>
    public async Task<RefuelingDefaults?> DefaultsAsync(Guid vehicleId, CancellationToken ct)
    {
        if (await VisibleVehicleAsync(vehicleId, ct) is null) return null;
        var latest = await odometer.LatestAsync(vehicleId, ct);
        var last = (await refuelings.ListForVehicleAsync(vehicleId, new RefuelingQuery(RefuelingSortField.Date, SortDirection.Desc, 0, 1), ct)).FirstOrDefault();
        return new RefuelingDefaults(latest?.Value, latest?.Date, last?.Currency);
    }

    /// <summary>Null when the log does not exist or the user may not see it.</summary>
    public async Task<Refueling?> FindAsync(Guid id, CancellationToken ct)
    {
        var refueling = await refuelings.FindAsync(id, ct);
        return refueling is not null && await VisibleVehicleAsync(refueling.VehicleId, ct) is not null ? refueling : null;
    }

    /// <param name="recalculateConsumption">Set to false when adding many logs in a row (an import) and call <see cref="RecalculateConsumptionAsync"/> once at the end.</param>
    public async Task<Refueling> LogAsync(Guid vehicleId, RefuelingInput input, CancellationToken ct, bool recalculateConsumption = true)
    {
        var (vehicle, _) = await EditableVehicleAsync(vehicleId, ct);
        var creator = await access.RequirePrincipalAsync(ct);
        await ValidateAsync(vehicle.Id, input, exceptReadingId: null, ct);

        var reading = OdometerReading.Create(vehicle.OwnerId, vehicle.Id, input.Date, input.Odometer);
        var cost = Cost.Create(vehicle.OwnerId, vehicle.Id, input.Date, input.TotalCost, input.Currency);
        var refueling = Refueling.Create(vehicle.OwnerId, creator.Id, vehicle.Id, input.Date, input.Volume, cost, reading, input.IsFullTank, input.Note);
        await refuelings.AddAsync(refueling, ct);
        if (recalculateConsumption) await RecalculateConsumptionAsync(vehicle.Id, ct);
        return await refuelings.FindAsync(refueling.Id, ct) ?? refueling;
    }

    public async Task<Refueling> UpdateAsync(Guid id, RefuelingInput input, CancellationToken ct)
    {
        var refueling = await EditableLogAsync(id, includeDeleted: false, ct);
        await ValidateAsync(refueling.VehicleId, input, exceptReadingId: refueling.OdometerReadingId, ct);

        refueling.Update(input.Date, input.Volume, input.TotalCost, input.Currency ?? refueling.Currency, input.Odometer, input.IsFullTank, input.Note);
        await refuelings.UpdateAsync(refueling, ct);
        await RecalculateConsumptionAsync(refueling.VehicleId, ct);
        return await refuelings.FindAsync(id, ct) ?? refueling;
    }

    /// <summary>Moves the log to the trash; it can be restored until it is deleted permanently.</summary>
    public async Task<Refueling> DeleteAsync(Guid id, CancellationToken ct)
    {
        var refueling = await EditableLogAsync(id, includeDeleted: false, ct);
        refueling.MarkDeleted(clock.GetUtcNow());
        await refuelings.UpdateAsync(refueling, ct);
        await RecalculateConsumptionAsync(refueling.VehicleId, ct); // the neighbours' fill-up intervals change
        return refueling;
    }

    public async Task<Refueling> RestoreAsync(Guid id, CancellationToken ct)
    {
        var refueling = await EditableLogAsync(id, includeDeleted: true, ct);
        await ValidateAsync(refueling.VehicleId, new RefuelingInput(refueling.Date, refueling.Volume, refueling.TotalCost, refueling.Currency, refueling.Odometer, refueling.IsFullTank, refueling.Note), exceptReadingId: null, ct);
        refueling.Restore();
        await refuelings.UpdateAsync(refueling, ct);
        await RecalculateConsumptionAsync(refueling.VehicleId, ct);
        return await refuelings.FindAsync(id, ct) ?? refueling;
    }

    /// <summary>
    /// Refreshes the stored consumption of every log of the vehicle (see <see cref="ConsumptionCalculator"/>) and saves only the ones that
    /// changed. Runs whenever a log is added, changed, trashed or restored, because a change moves the boundaries between full fill-ups.
    /// </summary>
    public async Task RecalculateConsumptionAsync(Guid vehicleId, CancellationToken ct)
    {
        var logs = await refuelings.ListAllForVehicleAsync(vehicleId, ct);
        await refuelings.SaveConsumptionsAsync(ConsumptionCalculator.Apply(logs), ct);
    }

    /// <summary>Trashed logs the user could restore.</summary>
    public async Task<IReadOnlyList<Refueling>> ListTrashAsync(RefuelingQuery query, CancellationToken ct) =>
        await refuelings.ListDeletedAsync(await access.LogScopeAsync(AccessLevel.Edit, ct), query.Normalized(), ct);

    public async Task<int> CountTrashAsync(CancellationToken ct) =>
        await refuelings.CountDeletedAsync(await access.LogScopeAsync(AccessLevel.Edit, ct), ct);

    /// <summary>How many trashed logs the user may delete permanently.</summary>
    public async Task<int> CountDeletableTrashAsync(CancellationToken ct) =>
        await refuelings.CountDeletedAsync(await access.LogScopeAsync(AccessLevel.Delete, ct), ct);

    /// <summary>Permanently removes the trashed logs the user has Delete access to. Returns how many were removed.</summary>
    public async Task<int> EmptyTrashAsync(CancellationToken ct)
    {
        var purged = await refuelings.PurgeAsync(await access.LogScopeAsync(AccessLevel.Delete, ct), ct);
        await photos.DeleteFilesAsync(LogType.Refueling, purged, ct); // their photos go with them
        return purged.Count;
    }

    /// <summary>What the current user may do with a vehicle's logs (the UI shows the matching buttons).</summary>
    public async Task<AccessLevel> LevelForVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct) ?? await vehicles.FindIncludingDeletedAsync(vehicleId, ct);
        return vehicle is null ? AccessLevel.None : await access.LogLevelAsync(vehicle, ct);
    }

    private async Task<Vehicle?> VisibleVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        return vehicle is not null && await access.LogLevelAsync(vehicle, ct) >= AccessLevel.View ? vehicle : null;
    }

    /// <summary>A visible vehicle the user may add logs to; others look missing, view-only users are forbidden.</summary>
    private async Task<(Vehicle Vehicle, AccessLevel Level)> EditableVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var level = vehicle is null ? AccessLevel.None : await access.LogLevelAsync(vehicle, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId });
        if (level < AccessLevel.Edit) throw new ForbiddenException("vehicle.viewOnly", "You may only view this vehicle.");
        return (vehicle, level);
    }

    private async Task<Refueling> EditableLogAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        var refueling = includeDeleted ? await refuelings.FindIncludingDeletedAsync(id, ct) : await refuelings.FindAsync(id, ct);
        var vehicle = refueling is null ? null : await vehicles.FindAsync(refueling.VehicleId, ct);
        var level = vehicle is null ? AccessLevel.None : await access.LogLevelAsync(vehicle, ct);
        if (refueling is null || level < AccessLevel.View) throw new NotFoundException("refueling.notFound", $"Refuelling {id} does not exist.", new { Id = id });
        if (level < AccessLevel.Edit) throw new ForbiddenException("vehicle.viewOnly", "You may only view this vehicle.");
        return refueling;
    }

    private async Task ValidateAsync(Guid vehicleId, RefuelingInput input, Guid? exceptReadingId, CancellationToken ct)
    {
        if (input.Date > LatestAllowedDate) throw new DomainException("refueling.dateInFuture", "The date cannot be in the future.");
        await odometer.ValidateAsync(vehicleId, input.Date, input.Odometer, exceptReadingId, ct);
    }
}
