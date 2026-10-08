using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Sync;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Sync;

namespace Tankstat.Api.GraphQL;

/// <summary>
/// One change a device kept while the server was out of reach: its own id, the version of what it changes it was made from, and exactly
/// one operation, with the same input as the single mutation (an add carries the id the device gave it).
/// </summary>
public sealed record ChangeInput(
    Guid Id, int? ExpectedVersion = null,
    LogRefuelingInput? LogRefueling = null, UpdateRefuelingInput? UpdateRefueling = null, Guid? DeleteRefueling = null, Guid? RestoreRefueling = null,
    AddExpenseInput? AddExpense = null, UpdateExpenseInput? UpdateExpense = null, Guid? DeleteExpense = null, Guid? RestoreExpense = null,
    AddRecurringExpenseInput? AddRecurringExpense = null, UpdateRecurringExpenseInput? UpdateRecurringExpense = null, Guid? DeleteRecurringExpense = null,
    MarkRecurringExpensesDoneInput? MarkRecurringExpensesDone = null,
    AddVehicleInput? AddVehicle = null, UpdateVehicleInput? UpdateVehicle = null, Guid? DeleteVehicle = null, Guid? RestoreVehicle = null);

/// <param name="Changes">In the order they were made (1 to 200).</param>
public sealed record SyncChangesInput(IReadOnlyList<ChangeInput> Changes);

public sealed record SyncReasonArg(string Name, string Value);

/// <summary>Why the server could not apply a change: the same error key (and arguments) as the single mutation would have given.</summary>
public sealed record SyncReasonInfo(string Key, IReadOnlyList<SyncReasonArg> Args);

/// <param name="EntityId">What it added or changed (applied changes).</param>
/// <param name="Version">Its version after the change.</param>
public sealed record SyncChangeResultInfo(Guid Id, SyncChangeStatus Status, Guid? EntityId, int? Version, SyncReasonInfo? Reason);

public sealed record SyncResultInfo(IReadOnlyList<SyncChangeResultInfo> Results, int Applied, int Parked);

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class SyncMutations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Applies the changes a device kept while the server was out of reach, in order, each as its single mutation would (every rule and
    /// access check), from the version it was made from. What cannot be applied is parked with the reason, never lost. A change sent again
    /// is answered with what happened the first time.
    /// </summary>
    public async Task<SyncResultInfo> SyncChanges(
        SyncChangesInput input, [Service] SyncService sync, [Service] VehicleService vehicles, [Service] RefuelingService refuelings, [Service] ExpenseService expenses,
        [Service] RecurringExpenseService recurring, [Service] IOptions<VehicleDefaultsOptions> defaults, [Service] IRefuelingRepository refuelingRows,
        [Service] IExpenseRepository expenseRows, [Service] IRecurringExpenseRepository scheduleRows, CancellationToken ct)
    {
        var find = new VehicleFinder(refuelingRows, expenseRows, scheduleRows);
        var requests = input.Changes.Select(change => Request(change, vehicles, refuelings, expenses, recurring, defaults.Value, find)).ToList();
        var outcome = await sync.SyncAsync(requests, ct);
        return new SyncResultInfo(
            outcome.Results.Select(r => new SyncChangeResultInfo(r.Id, r.Status, r.EntityId, r.Version,
                r.ReasonKey is null ? null : new SyncReasonInfo(r.ReasonKey, r.ReasonArgs.Select(a => new SyncReasonArg(a.Key, a.Value)).ToList()))).ToList(),
            outcome.Applied, outcome.Parked);
    }

    /// <summary>The vehicle of what a change concerns, whoever may see it: a parked change is filed with it.</summary>
    private sealed record VehicleFinder(IRefuelingRepository Refuelings, IExpenseRepository Expenses, IRecurringExpenseRepository Schedules)
    {
        public Func<CancellationToken, Task<Guid?>> Refueling(Guid id) => async ct => (await Refuelings.FindIncludingDeletedAsync(id, ct))?.VehicleId;
        public Func<CancellationToken, Task<Guid?>> Expense(Guid id) => async ct => (await Expenses.FindIncludingDeletedAsync(id, ct))?.VehicleId;
        public Func<CancellationToken, Task<Guid?>> Schedule(Guid id) => async ct => (await Schedules.FindAsync(id, ct))?.VehicleId;
    }

    private static SyncChangeRequest Request(
        ChangeInput c, VehicleService vehicles, RefuelingService refuelings, ExpenseService expenses, RecurringExpenseService recurring, VehicleDefaultsOptions defaults,
        VehicleFinder find)
    {
        var set = new object?[]
        {
            c.LogRefueling, c.UpdateRefueling, c.DeleteRefueling, c.RestoreRefueling, c.AddExpense, c.UpdateExpense, c.DeleteExpense, c.RestoreExpense,
            c.AddRecurringExpense, c.UpdateRecurringExpense, c.DeleteRecurringExpense, c.MarkRecurringExpensesDone, c.AddVehicle, c.UpdateVehicle,
            c.DeleteVehicle, c.RestoreVehicle,
        }.Count(o => o is not null);
        if (set != 1) throw new DomainException("sync.oneOperationRequired", "A change carries exactly one operation.", new { c.Id });
        var payload = JsonSerializer.Serialize(c, Json);
        var version = c.ExpectedVersion;

        static Guid Required(Guid? id, Guid change) =>
            id is { } given && given != Guid.Empty ? given : throw new DomainException("sync.entityIdRequired", "An add sent by a device names the id it gave it.", new { Id = change });
        static AppliedChange Of<T>(T entity, Func<T, Guid> id, Func<T, int> v, Func<T, Guid> vehicle) => new(id(entity), v(entity), vehicle(entity));

        SyncChangeRequest Make(
            SyncChangeKind kind, Guid? target, Guid? vehicleHint, Func<CancellationToken, Task<AppliedChange>> apply, Func<CancellationToken, Task<Guid?>>? vehicleOf = null) =>
            new(c.Id, kind, target, vehicleHint, version, payload, apply, vehicleOf);

        if (c.LogRefueling is { } lr)
        {
            var id = Required(lr.Id, c.Id);
            return Make(SyncChangeKind.LogRefueling, id, lr.VehicleId, async ct => Of(await refuelings.LogAsync(lr.VehicleId,
                new RefuelingInput(lr.Date, lr.Volume, lr.TotalCost, lr.Currency ?? defaults.Currency, lr.Odometer, lr.IsFullTank, lr.Note, lr.MissedPreviousFillUp), ct,
                photoDraftIds: lr.PhotoIds, id: id), r => r.Id, r => r.Version, r => r.VehicleId));
        }
        if (c.UpdateRefueling is { } ur)
            return Make(SyncChangeKind.UpdateRefueling, ur.Id, null, async ct => Of(await refuelings.UpdateAsync(ur.Id,
                new RefuelingInput(ur.Date, ur.Volume, ur.TotalCost, ur.Currency, ur.Odometer, ur.IsFullTank, ur.Note, ur.MissedPreviousFillUp), ct, version),
                r => r.Id, r => r.Version, r => r.VehicleId), find.Refueling(ur.Id));
        if (c.DeleteRefueling is { } dr)
            return Make(SyncChangeKind.DeleteRefueling, dr, null, async ct => Of(await refuelings.DeleteAsync(dr, ct, version), r => r.Id, r => r.Version, r => r.VehicleId), find.Refueling(dr));
        if (c.RestoreRefueling is { } rr)
            return Make(SyncChangeKind.RestoreRefueling, rr, null, async ct => Of(await refuelings.RestoreAsync(rr, ct, version), r => r.Id, r => r.Version, r => r.VehicleId), find.Refueling(rr));

        if (c.AddExpense is { } ae)
        {
            var id = Required(ae.Id, c.Id);
            return Make(SyncChangeKind.AddExpense, id, ae.VehicleId, async ct => Of(await expenses.AddAsync(ae.VehicleId,
                new ExpenseInput(ae.Date, ae.Title, ae.Category, ae.Amount, ae.Currency ?? defaults.Currency, ae.Odometer, ae.Note), ct, ae.PhotoIds, id),
                e => e.Id, e => e.Version, e => e.VehicleId));
        }
        if (c.UpdateExpense is { } ue)
            return Make(SyncChangeKind.UpdateExpense, ue.Id, null, async ct => Of(await expenses.UpdateAsync(ue.Id,
                new ExpenseInput(ue.Date, ue.Title, ue.Category, ue.Amount, ue.Currency, ue.Odometer, ue.Note), ct, version), e => e.Id, e => e.Version, e => e.VehicleId), find.Expense(ue.Id));
        if (c.DeleteExpense is { } de)
            return Make(SyncChangeKind.DeleteExpense, de, null, async ct => Of(await expenses.DeleteAsync(de, ct, version), e => e.Id, e => e.Version, e => e.VehicleId), find.Expense(de));
        if (c.RestoreExpense is { } re)
            return Make(SyncChangeKind.RestoreExpense, re, null, async ct => Of(await expenses.RestoreAsync(re, ct, version), e => e.Id, e => e.Version, e => e.VehicleId), find.Expense(re));

        if (c.AddRecurringExpense is { } ar)
        {
            var id = Required(ar.Id, c.Id);
            return Make(SyncChangeKind.AddRecurringExpense, id, ar.VehicleId, async ct => Of(await recurring.AddAsync(ar.VehicleId,
                new RecurringExpenseInput(ar.Title, ar.Category, ar.Note, ar.Kind, ar.IntervalMonths, ar.IntervalDistance, ar.LastDoneDate, ar.LastDoneOdometer, ar.WarnDays, ar.WarnDistance),
                ct, id), i => i.Item.Id, i => i.Item.Version, i => i.Item.VehicleId));
        }
        if (c.UpdateRecurringExpense is { } urc)
            return Make(SyncChangeKind.UpdateRecurringExpense, urc.Id, null, async ct => Of(await recurring.UpdateAsync(urc.Id,
                new RecurringExpenseInput(urc.Title, urc.Category, urc.Note, urc.Kind, urc.IntervalMonths, urc.IntervalDistance, urc.LastDoneDate, urc.LastDoneOdometer, urc.WarnDays, urc.WarnDistance),
                ct, version), i => i.Item.Id, i => i.Item.Version, i => i.Item.VehicleId), find.Schedule(urc.Id));
        if (c.DeleteRecurringExpense is { } drc)
            return Make(SyncChangeKind.DeleteRecurringExpense, drc, null, async ct =>
            {
                await recurring.DeleteAsync(drc, ct, version);
                return new AppliedChange(drc, null, null);
            }, find.Schedule(drc));
        if (c.MarkRecurringExpensesDone is { } md)
            return Make(SyncChangeKind.MarkRecurringExpensesDone, md.ExpenseId, null, async ct =>
            {
                var done = await recurring.MarkDoneAsync(md.Ids,
                    new MarkDoneInput(md.Date, md.Odometer, md.Amount, md.Currency ?? defaults.Currency, md.Title, md.Category, md.PhotoIds, md.ExpenseId), ct);
                return new AppliedChange(done.Expense?.Id, done.Expense?.Version, done.Schedules.FirstOrDefault()?.Item.VehicleId);
            }, md.Ids.Count > 0 ? find.Schedule(md.Ids[0]) : null);

        if (c.AddVehicle is { } av)
        {
            var id = Required(av.Id, c.Id);
            return Make(SyncChangeKind.AddVehicle, id, id, async ct => Of(await vehicles.AddAsync(av.Name, av.LicensePlate, av.FuelType,
                av.Units?.ToDomain() ?? MeasurementUnits.Create(defaults.DistanceUnit, defaults.VolumeUnit), ct, id), v => v.Id, v => v.Version, v => v.Id));
        }
        if (c.UpdateVehicle is { } uv)
            return Make(SyncChangeKind.UpdateVehicle, uv.Id, uv.Id, async ct => Of(await vehicles.UpdateAsync(uv.Id, uv.Name, uv.LicensePlate, uv.FuelType, uv.Units?.ToDomain(), ct, version),
                v => v.Id, v => v.Version, v => v.Id));
        if (c.DeleteVehicle is { } dv)
            return Make(SyncChangeKind.DeleteVehicle, dv, dv, async ct => Of(await vehicles.DeleteAsync(dv, ct, version), v => v.Id, v => v.Version, v => v.Id));
        var rv = c.RestoreVehicle!.Value;
        return Make(SyncChangeKind.RestoreVehicle, rv, rv, async ct => Of(await vehicles.RestoreAsync(rv, ct, version), v => v.Id, v => v.Version, v => v.Id));
    }
}
