using System.Text.Json.Serialization;
using System.Text.Json;
using GreenDonut;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Expenses;
using Tankstat.Application.Photos;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Sync;
using Tankstat.Application.Vehicles;
using Tankstat.Application;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Vehicles;
using Tankstat.Domain;

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
    AddVehicleInput? AddVehicle = null, UpdateVehicleInput? UpdateVehicle = null, Guid? DeleteVehicle = null, Guid? RestoreVehicle = null,
    AddLogPhotoInput? AddRefuelingPhoto = null, RemoveLogPhotoInput? RemoveRefuelingPhoto = null,
    AddLogPhotoInput? AddExpensePhoto = null, RemoveLogPhotoInput? RemoveExpensePhoto = null);

/// <summary>A photo added to a saved log while offline: uploaded as a draft of the log's vehicle (<c>PUT /media/vehicles/{id}/photo-drafts</c>) just before.</summary>
public sealed record AddLogPhotoInput(Guid LogId, Guid DraftId);

/// <summary>A photo of a saved log removed while offline.</summary>
public sealed record RemoveLogPhotoInput(Guid LogId, Guid ImageId);

/// <param name="Changes">In the order they were made (1 to 200).</param>
public sealed record SyncChangesInput(IReadOnlyList<ChangeInput> Changes);

public sealed record SyncReasonArg(string Name, string Value);

/// <summary>Why the server could not apply a change: the same error key (and arguments) as the single mutation would have given.</summary>
public sealed record SyncReasonInfo(string Key, IReadOnlyList<SyncReasonArg> Args);

/// <param name="EntityId">What it added or changed (applied changes).</param>
/// <param name="Version">Its version after the change.</param>
public sealed record SyncChangeResultInfo(Guid Id, SyncChangeStatus Status, Guid? EntityId, int? Version, SyncReasonInfo? Reason);

public sealed record SyncResultInfo(IReadOnlyList<SyncChangeResultInfo> Results, int Applied, int Parked);

/// <summary>
/// A change sent from a device that the server parked (or that someone decided about): what it is, the change itself as the device sent it
/// (`change`, JSON of a <c>ChangeInput</c>), why it was parked, and who sent it and when.
/// </summary>
public sealed record SyncChangeInfo(
    Guid Id, SyncChangeKind Kind, SyncChangeStatus Status, Guid? VehicleId, Guid? TargetId, int? ExpectedVersion, string Change, DateTimeOffset ReceivedAt,
    SyncReasonInfo? Reason, Guid SubmittedById, DateTimeOffset? ResolvedAt, Guid? EntityId, int? Version)
{
    public static SyncChangeInfo From(SyncChange c) => new(
        c.Id, c.Kind, c.Status, c.VehicleId, c.TargetId, c.ExpectedVersion, c.Payload, c.ReceivedAt,
        c.ReasonKey is null ? null : new SyncReasonInfo(c.ReasonKey, c.ReasonArgs.Select(a => new SyncReasonArg(a.Key, a.Value)).ToList()),
        c.SubmittedById, c.ResolvedAt, c.ResultId, c.ResultVersion);
}

[ExtendObjectType<SyncChangeInfo>]
public sealed class SyncChangeInfoExtensions
{
    public Task<UserRef?> GetSubmittedBy([Parent] SyncChangeInfo change, UserRefLoader users, CancellationToken ct) => users.LoadAsync(change.SubmittedById, ct);

    /// <summary>
    /// The vehicle's name and units, to show the change by (even a trashed vehicle, or one the sender no longer has access to: they saw it
    /// when they made the change). Null for a vehicle the server never took.
    /// </summary>
    public async Task<SyncVehicleRef?> GetVehicle([Parent] SyncChangeInfo change, VehicleIncludingDeletedLoader vehicles, CancellationToken ct) =>
        change.VehicleId is { } id && await vehicles.LoadAsync(id, ct) is { } v ? new SyncVehicleRef(v.Id, v.Name, v.Units.Distance, v.Units.Volume) : null;

    /// <summary>Whether the current user may apply or discard it: whoever sent it, or may change the vehicle's logs.</summary>
    public async Task<bool> GetCanResolve([Parent] SyncChangeInfo change, [Service] AccessService access, LogLevelByVehicleLoader levels, CancellationToken ct) =>
        change.SubmittedById == (await access.RequirePrincipalAsync(ct)).Id || (change.VehicleId is { } id && await levels.LoadAsync(id, ct) >= AccessLevel.Edit);
}

/// <summary>Just enough of a vehicle to name a change of it; nothing else of the vehicle is shown through a change.</summary>
public sealed record SyncVehicleRef(Guid Id, string Name, DistanceUnit DistanceUnit, VolumeUnit VolumeUnit);

/// <summary>APPLY: apply it anyway (the version it was made from no longer counts). DISCARD: it is never applied.</summary>
public enum SyncResolveAction
{
    Apply,
    Discard,
}

/// <param name="Change">For APPLY: the change as edited before applying (same kind); omit to apply it as it was sent.</param>
public sealed record ResolveSyncChangeInput(Guid Id, SyncResolveAction Action, ChangeInput? Change = null);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class SyncQueries
{
    /// <summary>The changes the server parked that the current user may see: of every vehicle whose logs they may view, and those they sent; newest first.</summary>
    public async Task<IReadOnlyList<SyncChangeInfo>> GetParkedChanges([Service] SyncService sync, CancellationToken ct) =>
        (await sync.ListParkedAsync(ct)).Select(SyncChangeInfo.From).ToList();
}

/// <summary>How many parked changes the vehicles of a response have, one query for all (for a vehicle the resolver already authorised).</summary>
public sealed class ParkedChangeCountLoader(SyncService sync, IBatchScheduler scheduler, DataLoaderOptions options) : BatchDataLoader<Guid, int>(scheduler, options)
{
    protected override async Task<IReadOnlyDictionary<Guid, int>> LoadBatchAsync(IReadOnlyList<Guid> keys, CancellationToken ct)
    {
        var counts = await sync.CountParkedAsync(keys, ct);
        return keys.ToDictionary(k => k, k => counts.GetValueOrDefault(k));
    }
}

[ExtendObjectType<Vehicle>]
public sealed class VehicleSyncExtensions
{
    /// <summary>How many changes sent from devices the server parked for this vehicle, waiting for someone to decide.</summary>
    public async Task<int> GetParkedChangeCount([Parent] Vehicle vehicle, ParkedChangeCountLoader loader, CancellationToken ct) => await loader.LoadAsync(vehicle.Id, ct);
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class SyncMutations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Enums as the API spells them (PETROL, US_GALLONS), so a stored change is a ChangeInput a client can read back as it is.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Applies the changes a device kept while the server was out of reach, in order, each as its single mutation would (every rule and
    /// access check), from the version it was made from. What cannot be applied is parked with the reason, never lost. A change sent again
    /// is answered with what happened the first time.
    /// </summary>
    public async Task<SyncResultInfo> SyncChanges(SyncChangesInput input, [Service] SyncService sync, [Service] SyncChangeServices services, CancellationToken ct)
    {
        var requests = input.Changes.Select(change => Request(change, services)).ToList();
        var outcome = await sync.SyncAsync(requests, ct);
        return new SyncResultInfo(
            outcome.Results.Select(r => new SyncChangeResultInfo(r.Id, r.Status, r.EntityId, r.Version,
                r.ReasonKey is null ? null : new SyncReasonInfo(r.ReasonKey, r.ReasonArgs.Select(a => new SyncReasonArg(a.Key, a.Value)).ToList()))).ToList(),
            outcome.Applied, outcome.Parked);
    }

    /// <summary>
    /// Applies a parked change anyway (as edited, when <c>change</c> is given) or discards it. Applying is the person's own change: every
    /// rule and access check applies, the version it was made from does not (they saw what is there now), and photos waiting since are
    /// not attached (they may be gone). A refusal keeps it parked with the new reason and is the error of this mutation.
    /// </summary>
    public async Task<SyncChangeInfo> ResolveSyncChange(
        ResolveSyncChangeInput input, [Service] SyncService sync, [Service] ISyncChangeRepository ledger, [Service] SyncChangeServices services, CancellationToken ct)
    {
        if (input.Action == SyncResolveAction.Discard) return SyncChangeInfo.From(await sync.ResolveAsync(input.Id, discard: true, null, null, ct));

        var stored = await ledger.FindAsync(input.Id, ct) ?? throw new NotFoundException("sync.notFound", "Change not found.", new { input.Id });
        var change = input.Change ?? JsonSerializer.Deserialize<ChangeInput>(stored.Payload, Json)!;
        // The same change (its id, so the ledger row is the same), from now on: no version to keep to, no photos that may be gone.
        change = change with
        {
            Id = stored.Id, ExpectedVersion = null,
            LogRefueling = change.LogRefueling is { } lr ? lr with { PhotoIds = null } : null,
            AddExpense = change.AddExpense is { } ae ? ae with { PhotoIds = null } : null,
            MarkRecurringExpensesDone = change.MarkRecurringExpensesDone is { } md ? md with { PhotoIds = null } : null,
        };
        var request = Request(change, services);
        if (request.Kind != stored.Kind) throw new DomainException("sync.kindMismatch", "An edited change does the same as the one parked.");
        var replacement = input.Change is null ? null : JsonSerializer.Serialize(change, Json);
        return SyncChangeInfo.From(await sync.ResolveAsync(input.Id, discard: false, request.Apply, replacement, ct));
    }

    private static SyncChangeRequest Request(ChangeInput c, SyncChangeServices services)
    {
        var (vehicles, refuelings, expenses, recurring, photos, defaults) = (services.Vehicles, services.Refuelings, services.Expenses, services.Recurring, services.Photos, services.Defaults);
        var find = services;
        var set = new object?[]
        {
            c.LogRefueling, c.UpdateRefueling, c.DeleteRefueling, c.RestoreRefueling, c.AddExpense, c.UpdateExpense, c.DeleteExpense, c.RestoreExpense,
            c.AddRecurringExpense, c.UpdateRecurringExpense, c.DeleteRecurringExpense, c.MarkRecurringExpensesDone, c.AddVehicle, c.UpdateVehicle,
            c.DeleteVehicle, c.RestoreVehicle, c.AddRefuelingPhoto, c.RemoveRefuelingPhoto, c.AddExpensePhoto, c.RemoveExpensePhoto,
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
        // Photos of saved logs: the log's version does not move (photo rows are not part of it), so nothing is checked against one.
        SyncChangeRequest AddPhoto(SyncChangeKind kind, LogType type, AddLogPhotoInput a, Func<Guid, Func<CancellationToken, Task<Guid?>>> vehicleOf) =>
            Make(kind, a.LogId, null, async ct => new AppliedChange(await photos.AttachDraftAsync(type, a.LogId, a.DraftId, ct), null, await vehicleOf(a.LogId)(ct)), vehicleOf(a.LogId));
        SyncChangeRequest RemovePhoto(SyncChangeKind kind, LogType type, RemoveLogPhotoInput r, Func<Guid, Func<CancellationToken, Task<Guid?>>> vehicleOf) =>
            Make(kind, r.LogId, null, async ct =>
            {
                await photos.RemoveAsync(type, r.LogId, r.ImageId, ct);
                return new AppliedChange(r.ImageId, null, await vehicleOf(r.LogId)(ct));
            }, vehicleOf(r.LogId));
        if (c.AddRefuelingPhoto is { } arp) return AddPhoto(SyncChangeKind.AddRefuelingPhoto, LogType.Refueling, arp, find.Refueling);
        if (c.RemoveRefuelingPhoto is { } rrp) return RemovePhoto(SyncChangeKind.RemoveRefuelingPhoto, LogType.Refueling, rrp, find.Refueling);
        if (c.AddExpensePhoto is { } aep) return AddPhoto(SyncChangeKind.AddExpensePhoto, LogType.Expense, aep, find.Expense);
        if (c.RemoveExpensePhoto is { } rep) return RemovePhoto(SyncChangeKind.RemoveExpensePhoto, LogType.Expense, rep, find.Expense);

        var rv = c.RestoreVehicle!.Value;
        return Make(SyncChangeKind.RestoreVehicle, rv, rv, async ct => Of(await vehicles.RestoreAsync(rv, ct, version), v => v.Id, v => v.Version, v => v.Id));
    }
}

/// <summary>
/// What a change from a device is applied through: the services of the single mutations, and the repositories that find the vehicle of
/// what a change concerns, whoever may see it (a parked change is filed with it).
/// </summary>
public sealed class SyncChangeServices(
    VehicleService vehicles, RefuelingService refuelings, ExpenseService expenses, RecurringExpenseService recurring, LogPhotoService photos,
    IOptions<VehicleDefaultsOptions> defaults, IRefuelingRepository refuelingRows, IExpenseRepository expenseRows, IRecurringExpenseRepository scheduleRows)
{
    public VehicleService Vehicles => vehicles;
    public RefuelingService Refuelings => refuelings;
    public ExpenseService Expenses => expenses;
    public RecurringExpenseService Recurring => recurring;
    public LogPhotoService Photos => photos;
    public VehicleDefaultsOptions Defaults => defaults.Value;

    public Func<CancellationToken, Task<Guid?>> Refueling(Guid id) => async ct => (await refuelingRows.FindIncludingDeletedAsync(id, ct))?.VehicleId;
    public Func<CancellationToken, Task<Guid?>> Expense(Guid id) => async ct => (await expenseRows.FindIncludingDeletedAsync(id, ct))?.VehicleId;
    public Func<CancellationToken, Task<Guid?>> Schedule(Guid id) => async ct => (await scheduleRows.FindAsync(id, ct))?.VehicleId;
}
