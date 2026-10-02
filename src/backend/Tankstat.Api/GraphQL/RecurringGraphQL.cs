using GreenDonut;
using Microsoft.Extensions.Options;
using Tankstat.Application.Recurring;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

/// <param name="LastDoneDate">Omit to start counting today.</param>
/// <param name="LastDoneOdometer">Omit to start from the vehicle's current odometer (its latest reading); needed when the vehicle has none and distance counts.</param>
/// <param name="WarnDays">Omit for the instance default (<c>vehicleDefaults.recurringWarnDays</c>).</param>
/// <param name="WarnDistance">Omit for the instance default (<c>vehicleDefaults.recurringWarnDistance</c>, in the vehicle's distance unit).</param>
public sealed record AddRecurringExpenseInput(
    Guid VehicleId, string Title, string? Category, string? Note, RecurrenceKind Kind, int? IntervalMonths, long? IntervalDistance,
    DateOnly? LastDoneDate, long? LastDoneOdometer, int? WarnDays, long? WarnDistance);

/// <param name="WarnDays">Omit to keep the current value.</param>
/// <param name="WarnDistance">Omit to keep the current value.</param>
public sealed record UpdateRecurringExpenseInput(
    Guid Id, string Title, string? Category, string? Note, RecurrenceKind Kind, int? IntervalMonths, long? IntervalDistance,
    DateOnly LastDoneDate, long? LastDoneOdometer, int? WarnDays, long? WarnDistance);

/// <param name="CreateExpense">Also log the cost as an expense (needs <c>amount</c>).</param>
/// <param name="Currency">ISO 4217 code; omit to use the instance default.</param>
public sealed record MarkRecurringExpenseDoneInput(Guid Id, DateOnly Date, long? Odometer, bool CreateExpense, decimal? Amount, string? Currency);

/// <summary>Where a recurring expense stands today. The days and distance left are negative once it is overdue; null when that side does not apply.</summary>
public sealed record RecurrenceStatusInfo(RecurrenceState State, RecurrenceLimit? Limit, DateOnly? DueDate, long? DueOdometer, int? DaysLeft, long? DistanceLeft);

/// <summary>A schedule such as insurance or an oil change, with its status and when it was added. Intervals and odometers are in the vehicle's distance unit.</summary>
public sealed record RecurringExpenseInfo(
    Guid Id, Guid VehicleId, string Title, string? Category, string? Note, RecurrenceKind Kind, int? IntervalMonths, long? IntervalDistance,
    DateOnly LastDoneDate, long? LastDoneOdometer, int WarnDays, long WarnDistance, DateTimeOffset CreatedAt, RecurrenceStatusInfo Status)
{
    public static RecurringExpenseInfo From(RecurringItem r) => new(
        r.Item.Id, r.Item.VehicleId, r.Item.Title, r.Item.Category, r.Item.Note, r.Item.Kind, r.Item.IntervalMonths, r.Item.IntervalDistance,
        r.Item.LastDoneDate, r.Item.LastDoneOdometer, r.Item.WarnDays, r.Item.WarnDistance, r.Item.CreatedAt,
        new RecurrenceStatusInfo(r.Status.State, r.Status.Limit, r.Status.DueDate, r.Status.DueOdometer, r.Status.DaysLeft, r.Status.DistanceLeft));
}

/// <summary>The recurring expenses of the vehicles of a response, loaded together (one access check, one query for the schedules, one for the odometers).</summary>
public sealed class RecurringByVehicleLoader(RecurringExpenseService recurring, IBatchScheduler scheduler, DataLoaderOptions options)
    : BatchDataLoader<Vehicle, IReadOnlyList<RecurringItem>>(scheduler, options)
{
    protected override async Task<IReadOnlyDictionary<Vehicle, IReadOnlyList<RecurringItem>>> LoadBatchAsync(IReadOnlyList<Vehicle> keys, CancellationToken ct)
    {
        var loaded = await recurring.ListForVehiclesAsync(keys, ct);
        return keys.ToDictionary(v => v, v => loaded[v.Id]);
    }
}

[ExtendObjectType<Vehicle>]
public sealed class VehicleRecurringExtensions
{
    /// <summary>The vehicle's recurring expenses, most urgent first.</summary>
    public async Task<IReadOnlyList<RecurringExpenseInfo>> GetRecurring([Parent] Vehicle vehicle, RecurringByVehicleLoader loader, CancellationToken ct) =>
        (await loader.LoadAsync(vehicle, ct) ?? []).Select(RecurringExpenseInfo.From).ToList();
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class RecurringMutations
{
    public async Task<RecurringExpenseInfo> AddRecurringExpense(AddRecurringExpenseInput input, [Service] RecurringExpenseService recurring, CancellationToken ct) =>
        RecurringExpenseInfo.From(await recurring.AddAsync(input.VehicleId,
            new RecurringExpenseInput(input.Title, input.Category, input.Note, input.Kind, input.IntervalMonths, input.IntervalDistance, input.LastDoneDate, input.LastDoneOdometer, input.WarnDays, input.WarnDistance), ct));

    public async Task<RecurringExpenseInfo> UpdateRecurringExpense(UpdateRecurringExpenseInput input, [Service] RecurringExpenseService recurring, CancellationToken ct) =>
        RecurringExpenseInfo.From(await recurring.UpdateAsync(input.Id,
            new RecurringExpenseInput(input.Title, input.Category, input.Note, input.Kind, input.IntervalMonths, input.IntervalDistance, input.LastDoneDate, input.LastDoneOdometer, input.WarnDays, input.WarnDistance), ct));

    /// <summary>Removes the schedule for good (expenses already logged from it stay).</summary>
    public async Task<bool> DeleteRecurringExpense(Guid id, [Service] RecurringExpenseService recurring, CancellationToken ct)
    {
        await recurring.DeleteAsync(id, ct);
        return true;
    }

    /// <summary>Starts the next interval from the given day and odometer and, when asked, logs the cost as an expense.</summary>
    public async Task<RecurringExpenseInfo> MarkRecurringExpenseDone(
        MarkRecurringExpenseDoneInput input, [Service] RecurringExpenseService recurring, [Service] IOptions<VehicleDefaultsOptions> defaults, CancellationToken ct) =>
        RecurringExpenseInfo.From(await recurring.MarkDoneAsync(input.Id, new MarkDoneInput(input.Date, input.Odometer, input.CreateExpense, input.Amount, input.Currency ?? defaults.Value.Currency), ct));
}
