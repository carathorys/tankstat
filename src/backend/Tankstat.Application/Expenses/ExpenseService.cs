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

namespace Tankstat.Application.Expenses;

/// <param name="Currency">Required when adding; when updating, omit it to keep the expense's currency.</param>
/// <param name="Odometer">Null when the odometer was not noted.</param>
public sealed record ExpenseInput(DateOnly Date, string Title, string? Category, decimal Amount, string? Currency, long? Odometer, string? Note);

/// <summary>
/// Expenses of a vehicle (service, insurance, ...). They follow the access rules of the vehicle's logs exactly like refuelings:
/// Edit may add, change, trash and restore; only Delete may delete permanently.
/// </summary>
public sealed class ExpenseService(
    LogAccessGuard guard, IExpenseRepository expenses, AccessService access, OdometerService odometer, LogPhotoService photos, TimeProvider clock)
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

    public async Task<Expense> AddAsync(Guid vehicleId, ExpenseInput input, CancellationToken ct)
    {
        var vehicle = await EditableVehicleAsync(vehicleId, ct);
        var creator = await access.RequirePrincipalAsync(ct);
        await ValidateAsync(vehicle.Id, input, exceptReadingId: null, ct);

        var expense = Build(vehicle, creator.Id, input);
        await expenses.AddAsync(expense, ct);
        return expense;
    }

    /// <summary>Builds (without saving) an expense whose input was already validated; used by <see cref="AddAsync"/> and by imports.</summary>
    public static Expense Build(Vehicle vehicle, Guid createdById, ExpenseInput input)
    {
        var cost = Cost.Create(vehicle.OwnerId, vehicle.Id, input.Date, input.Amount, input.Currency);
        var reading = input.Odometer is { } value ? OdometerReading.Create(vehicle.OwnerId, vehicle.Id, input.Date, value) : null;
        return Expense.Create(vehicle.OwnerId, createdById, vehicle.Id, input.Date, input.Title, input.Category, cost, reading, input.Note);
    }

    public async Task<Expense> UpdateAsync(Guid id, ExpenseInput input, CancellationToken ct)
    {
        var expense = await EditableAsync(id, includeDeleted: false, ct);
        await ValidateAsync(expense.VehicleId, input, exceptReadingId: expense.OdometerReadingId, ct);

        var removed = expense.Update(input.Date, input.Title, input.Category, input.Amount, input.Currency ?? expense.Currency, input.Odometer, input.Note, out var created);
        await expenses.UpdateAsync(expense, created, removed, ct);
        return expense;
    }

    /// <summary>Moves the expense to the trash; it can be restored until it is deleted permanently.</summary>
    public async Task<Expense> DeleteAsync(Guid id, CancellationToken ct)
    {
        var expense = await EditableAsync(id, includeDeleted: false, ct);
        expense.MarkDeleted(clock.GetUtcNow());
        await expenses.UpdateAsync(expense, null, null, ct);
        return expense;
    }

    public async Task<Expense> RestoreAsync(Guid id, CancellationToken ct)
    {
        var expense = await EditableAsync(id, includeDeleted: true, ct);
        if (expense.Odometer is { } value) await odometer.ValidateAsync(expense.VehicleId, expense.Date, value, exceptReadingId: null, ct);
        expense.Restore();
        await expenses.UpdateAsync(expense, null, null, ct);
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
        var purged = await expenses.PurgeAsync(await access.LogScopeAsync(AccessLevel.Delete, ct), ct);
        await photos.DeleteFilesAsync(LogType.Expense, purged, ct); // their photos go with them
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
