using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Odometers;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Recurring;

/// <param name="LastDoneDate">When adding, omit to start counting today; when updating, omit to keep it.</param>
/// <param name="LastDoneOdometer">When adding a schedule that counts distance, omit to start from the vehicle's current odometer (its latest reading).</param>
/// <param name="WarnDays">Omit for the default (30 days) when adding, or to keep the current value when updating.</param>
/// <param name="WarnDistance">Omit for the default (500 distance units of the vehicle) when adding, or to keep the current value when updating.</param>
public sealed record RecurringExpenseInput(
    string Title, string? Category, string? Note, RecurrenceKind Kind, int? IntervalMonths, long? IntervalDistance,
    DateOnly? LastDoneDate, long? LastDoneOdometer, int? WarnDays, long? WarnDistance);

/// <param name="CreateExpense">Also log the cost as a normal expense (needs <paramref name="Amount"/> and <paramref name="Currency"/>).</param>
public sealed record MarkDoneInput(DateOnly Date, long? Odometer, bool CreateExpense, decimal? Amount, string? Currency);

/// <summary>A recurring expense with where it stands today.</summary>
public sealed record RecurringItem(RecurringExpense Item, RecurrenceStatus Status);

/// <summary>
/// The recurring expenses of a vehicle (schedules such as insurance or an oil change). They follow the access rules of the vehicle's
/// logs like expenses do: View sees them, Edit changes them. Marking one done logs the cost as a normal expense, so every expense and
/// odometer rule applies, and starts the next interval.
/// </summary>
public sealed class RecurringExpenseService(
    LogAccessGuard guard, IRecurringExpenseRepository items, AccessService access, OdometerService odometer, ExpenseService expenses, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    private DateOnly LatestAllowedDate => Today.AddDays(1); // a day ahead covers every time zone, like the expenses

    /// <summary>The vehicle's recurring expenses, most urgent first; empty when the vehicle does not exist or may not be seen.</summary>
    public async Task<IReadOnlyList<RecurringItem>> ListAsync(Guid vehicleId, CancellationToken ct)
    {
        if (await VisibleVehicleAsync(vehicleId, ct) is null) return [];
        var current = (await odometer.LatestAsync(vehicleId, ct))?.Value;
        return (await items.ListForVehicleAsync(vehicleId, ct))
            .Select(i => new RecurringItem(i, RecurrenceCalculator.Evaluate(i, Today, current)))
            .OrderByDescending(r => r.Status.State)
            .ThenBy(r => r.Status.DueDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RecurringItem> AddAsync(Guid vehicleId, RecurringExpenseInput input, CancellationToken ct)
    {
        var vehicle = await EditableVehicleAsync(vehicleId, ct);
        var creator = await access.RequirePrincipalAsync(ct);
        var start = input.LastDoneDate ?? Today;
        CheckDate(start);
        // Counting starts now, so the odometer is the one the vehicle has now, unless the person says otherwise.
        var startOdometer = input.LastDoneOdometer;
        if (startOdometer is null && input.Kind is RecurrenceKind.Odometer or RecurrenceKind.Combined) startOdometer = (await odometer.LatestAsync(vehicle.Id, ct))?.Value;

        var item = RecurringExpense.Create(
            vehicle.OwnerId, creator.Id, vehicle.Id, input.Title, input.Category, input.Note, input.Kind, input.IntervalMonths, input.IntervalDistance,
            start, startOdometer, input.WarnDays ?? RecurringExpense.DefaultWarnDays, input.WarnDistance ?? RecurringExpense.DefaultWarnDistance, clock.GetUtcNow());
        await items.AddAsync(item, ct);
        return await WithStatusAsync(item, ct);
    }

    public async Task<RecurringItem> UpdateAsync(Guid id, RecurringExpenseInput input, CancellationToken ct)
    {
        var item = await EditableAsync(id, ct);
        var lastDone = input.LastDoneDate ?? item.LastDoneDate;
        CheckDate(lastDone);

        item.Update(
            input.Title, input.Category, input.Note, input.Kind, input.IntervalMonths, input.IntervalDistance, lastDone, input.LastDoneOdometer,
            input.WarnDays ?? item.WarnDays, input.WarnDistance ?? item.WarnDistance);
        await items.UpdateAsync(item, ct);
        return await WithStatusAsync(item, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct) => await items.RemoveAsync(await EditableAsync(id, ct), ct);

    /// <summary>Starts the next interval from the given day and odometer, and (when asked) logs what it cost as an expense.</summary>
    public async Task<RecurringItem> MarkDoneAsync(Guid id, MarkDoneInput input, CancellationToken ct)
    {
        var item = await EditableAsync(id, ct);
        CheckDate(input.Date);
        if (input.CreateExpense && (input.Amount is null || string.IsNullOrWhiteSpace(input.Currency)))
            throw new DomainException("recurring.amountRequired", "The amount and currency are needed to log the expense.");

        item.CheckDone(input.Date, input.Odometer); // the schedule's own rules first: nothing is logged for a day it would refuse
        var logged = input.CreateExpense
            ? await expenses.AddAsync(item.VehicleId, new ExpenseInput(input.Date, item.Title, item.Category, input.Amount!.Value, input.Currency, input.Odometer, item.Note), ct)
            : null;
        try
        {
            item.MarkDone(input.Date, input.Odometer);
            await items.UpdateAsync(item, ct);
        }
        catch
        {
            // The expense and the schedule are saved separately, so undo the expense (to the trash) when the schedule could not move on:
            // the schedule is then still due, and a second try does not log the cost twice.
            if (logged is not null) await TryTrashAsync(logged.Id);
            throw;
        }
        return await WithStatusAsync(item, ct);
    }

    private async Task TryTrashAsync(Guid expenseId)
    {
        try { await expenses.DeleteAsync(expenseId, CancellationToken.None); }
        catch { /* best effort: the original failure is the one to report */ }
    }

    private void CheckDate(DateOnly date)
    {
        if (date > LatestAllowedDate) throw new DomainException("refueling.dateInFuture", "The date cannot be in the future.");
    }

    private async Task<RecurringItem> WithStatusAsync(RecurringExpense item, CancellationToken ct) =>
        new(item, RecurrenceCalculator.Evaluate(item, Today, (await odometer.LatestAsync(item.VehicleId, ct))?.Value));

    private async Task<Vehicle?> VisibleVehicleAsync(Guid vehicleId, CancellationToken ct) => (await guard.ForVehicleAsync(vehicleId, ct))?.Vehicle;

    private async Task<Vehicle> EditableVehicleAsync(Guid vehicleId, CancellationToken ct) =>
        LogAccessGuard.RequireEdit(await guard.ForVehicleAsync(vehicleId, ct),
            () => new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId })).Vehicle;

    private async Task<RecurringExpense> EditableAsync(Guid id, CancellationToken ct)
    {
        var item = await items.FindAsync(id, ct);
        LogAccessGuard.RequireEdit(await guard.ForVehicleAsync(item?.VehicleId, ct),
            () => new NotFoundException("recurring.notFound", $"Recurring expense {id} does not exist.", new { Id = id }));
        return item!;
    }
}
