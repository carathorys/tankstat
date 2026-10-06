using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Odometers;
using Tankstat.Application.Photos;
using Tankstat.Application.Recognition;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Expenses;

/// <param name="Amount">Null only while a photo of the expense is still being read: it fills the amount in later.</param>
/// <param name="Currency">Required with an amount when adding; when updating, omit it to keep the expense's currency.</param>
/// <param name="Odometer">Null when the odometer was not noted (a photo that is still being read may fill it in).</param>
public sealed record ExpenseInput(DateOnly Date, string Title, string? Category, decimal? Amount, string? Currency, long? Odometer, string? Note);

/// <summary>
/// Expenses of a vehicle (service, insurance, ...). They follow the access rules of the vehicle's logs exactly like refuelings:
/// Edit may add, change, trash and restore; only Delete may delete permanently.
/// </summary>
public sealed class ExpenseService(
    LogAccessGuard guard, IExpenseRepository expenses, AccessService access, OdometerService odometer, LogPhotoService photos, LogPhotoFiller filler,
    TimeProvider clock, ILogger<ExpenseService> logger)
{
    private DateOnly LatestAllowedDate => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);

    public async Task<IReadOnlyList<Expense>> ListAsync(Guid vehicleId, ExpenseQuery query, CancellationToken ct) =>
        await VisibleVehicleAsync(vehicleId, ct) is null ? [] : await expenses.ListForVehicleAsync(vehicleId, query.Normalized(), ct);

    public async Task<int> CountAsync(Guid vehicleId, CancellationToken ct) =>
        await VisibleVehicleAsync(vehicleId, ct) is null ? 0 : await expenses.CountForVehicleAsync(vehicleId, ct);

    public async Task<IReadOnlyList<string>> CategoriesAsync(Guid vehicleId, CancellationToken ct) =>
        await VisibleVehicleAsync(vehicleId, ct) is null ? [] : await expenses.CategoriesAsync(vehicleId, ct);

    public async Task<Expense?> FindAsync(Guid id, CancellationToken ct)
    {
        var expense = await expenses.FindAsync(id, ct);
        return expense is not null && await VisibleVehicleAsync(expense.VehicleId, ct) is not null ? expense : null;
    }

    /// <param name="photoDraftIds">Photos the user uploaded for this expense before saving it (see <see cref="PhotoDraftService"/>).</param>
    public async Task<Expense> AddAsync(Guid vehicleId, ExpenseInput input, CancellationToken ct, IReadOnlyCollection<Guid>? photoDraftIds = null)
    {
        var vehicle = await EditableVehicleAsync(vehicleId, ct);
        var creator = await access.RequirePrincipalAsync(ct);
        await ValidateAsync(vehicle.Id, input, exceptReadingId: null, ct);
        var drafts = await photos.RequireDraftsAsync(vehicle.Id, photoDraftIds, ct);
        var readingPhotos = (input.Amount is null || input.Odometer is null) && await filler.MayWaitForDraftsAsync([.. drafts.Select(d => d.Id)], ct);

        var expense = Build(vehicle, creator.Id, input, readingPhotos);
        await expenses.AddAsync(expense, ct);
        await photos.AttachDraftsAsync(LogType.Expense, expense.Id, drafts, ct);
        logger.LogDebug("User {UserId} added expense {ExpenseId} for vehicle {VehicleId} with {Photos} photos", creator.Id, expense.Id, vehicle.Id, drafts.Count);
        // Readings that finished before the save are taken now; a draft that could not be attached leaves nothing to wait for.
        if (expense.ReviewState == ReviewState.AwaitingPhotos) await filler.FillAsync(LogType.Expense, expense.Id, ct);
        return await expenses.FindAsync(expense.Id, ct) ?? expense;
    }

    /// <summary>Builds (without saving) an expense whose input was already validated; used by <see cref="AddAsync"/> and by imports.</summary>
    /// <param name="readingPhotos">A photo of the expense is still being read (only then may the amount be empty).</param>
    public static Expense Build(Vehicle vehicle, Guid createdById, ExpenseInput input, bool readingPhotos = false)
    {
        var cost = input.Amount is { } amount ? Cost.Create(vehicle.OwnerId, vehicle.Id, input.Date, amount, input.Currency) : null;
        var reading = input.Odometer is { } value ? OdometerReading.Create(vehicle.OwnerId, vehicle.Id, input.Date, value) : null;
        return Expense.Create(vehicle.OwnerId, createdById, vehicle.Id, input.Date, input.Title, input.Category, cost, reading, input.Note, readingPhotos);
    }

    public async Task<Expense> UpdateAsync(Guid id, ExpenseInput input, CancellationToken ct)
    {
        var expense = await EditableAsync(id, includeDeleted: false, ct);
        await ValidateAsync(expense.VehicleId, input, exceptReadingId: expense.OdometerReadingId, ct);
        var readingPhotos = (input.Amount is null || input.Odometer is null) && await filler.MayWaitForLogPhotosAsync(LogType.Expense, id, ct);
        var waited = expense.ReviewState;

        var changes = expense.Update(input.Date, input.Title, input.Category, input.Amount, input.Currency ?? expense.Currency, input.Odometer, input.Note, readingPhotos);
        await expenses.UpdateAsync(expense, changes, ct);
        if (waited != ReviewState.None && expense.ReviewState == ReviewState.None)
            await filler.ReviewedAsync(LogType.Expense, id, expense.CreatedById, ct);
        logger.LogDebug("Expense {ExpenseId} of vehicle {VehicleId} updated", id, expense.VehicleId);
        if (expense.ReviewState != ReviewState.AwaitingPhotos) return expense;
        // Readings that finished before the save are taken now (the worker's round may have passed while the log did not wait yet).
        await filler.FillAsync(LogType.Expense, id, ct);
        return await expenses.FindAsync(id, ct) ?? expense;
    }

    /// <summary>Moves the expense to the trash; it can be restored until it is deleted permanently.</summary>
    public async Task<Expense> DeleteAsync(Guid id, CancellationToken ct)
    {
        var expense = await EditableAsync(id, includeDeleted: false, ct);
        expense.MarkDeleted(clock.GetUtcNow());
        await expenses.UpdateAsync(expense, LinkedChanges.None, ct);
        logger.LogDebug("Expense {ExpenseId} of vehicle {VehicleId} moved to the trash", id, expense.VehicleId);
        return expense;
    }

    public async Task<Expense> RestoreAsync(Guid id, CancellationToken ct)
    {
        var expense = await EditableAsync(id, includeDeleted: true, ct);
        if (expense.Odometer is { } value) await odometer.ValidateAsync(expense.VehicleId, expense.Date, value, exceptReadingId: null, ct);
        expense.Restore();
        await expenses.UpdateAsync(expense, LinkedChanges.None, ct);
        logger.LogDebug("Expense {ExpenseId} of vehicle {VehicleId} restored from the trash", id, expense.VehicleId);
        if (expense.ReviewState == ReviewState.AwaitingPhotos)
        {
            await filler.FillAsync(LogType.Expense, id, ct); // its photos may have been read while it was in the trash
            return await expenses.FindAsync(id, ct) ?? expense;
        }
        return expense;
    }

    public async Task<IReadOnlyList<Expense>> ListTrashAsync(ExpenseQuery query, CancellationToken ct) =>
        await expenses.ListDeletedAsync(await access.LogScopeAsync(AccessLevel.Edit, ct), query.Normalized(), ct);

    public async Task<int> CountTrashAsync(CancellationToken ct) =>
        await expenses.CountDeletedAsync(await access.LogScopeAsync(AccessLevel.Edit, ct), ct);

    public async Task<int> CountDeletableTrashAsync(CancellationToken ct) =>
        await expenses.CountDeletedAsync(await access.LogScopeAsync(AccessLevel.Delete, ct), ct);

    /// <summary>Permanently removes the trashed expenses the user has Delete access to. Returns how many were removed.</summary>
    public async Task<int> EmptyTrashAsync(CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var purged = await expenses.PurgeAsync(await access.LogScopeAsync(AccessLevel.Delete, ct), ct);
        await photos.DeleteFilesAsync(LogType.Expense, purged, ct); // their photos go with them
        if (purged.Count > 0) logger.LogInformation("User {UserId} emptied the expense trash: {Count} expenses deleted for good ({WithPhotos} with photos)", user.Id, purged.Count, purged.WithPhotos.Count);
        return purged.Count;
    }

    private async Task<Vehicle?> VisibleVehicleAsync(Guid vehicleId, CancellationToken ct) => (await guard.ForVehicleAsync(vehicleId, ct))?.Vehicle;

    private async Task<Vehicle> EditableVehicleAsync(Guid vehicleId, CancellationToken ct) =>
        LogAccessGuard.RequireEdit(await guard.ForVehicleAsync(vehicleId, ct),
            () => new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId })).Vehicle;

    private async Task<Expense> EditableAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        var expense = includeDeleted ? await expenses.FindIncludingDeletedAsync(id, ct) : await expenses.FindAsync(id, ct);
        LogAccessGuard.RequireEdit(await guard.ForVehicleAsync(expense?.VehicleId, ct),
            () => new NotFoundException("expense.notFound", $"Expense {id} does not exist.", new { Id = id }));
        return expense!;
    }

    private async Task ValidateAsync(Guid vehicleId, ExpenseInput input, Guid? exceptReadingId, CancellationToken ct)
    {
        if (input.Date > LatestAllowedDate) throw new DomainException("refueling.dateInFuture", "The date cannot be in the future.");
        if (input.Odometer is { } value) await odometer.ValidateAsync(vehicleId, input.Date, value, exceptReadingId, ct);
    }
}
