using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Sync;

namespace Tankstat.Application.Sync;

/// <summary>What applying a change produced: the entity (an add's, an update's) with its new version, and the vehicle it belongs to.</summary>
public sealed record AppliedChange(Guid? EntityId, int? Version, Guid? VehicleId);

/// <summary>
/// One change of a batch: what it is, as the device sent it (`Payload`, JSON), and how to apply it through the services (`Apply`, built by
/// the API from the typed input; every rule and access check of the single mutations applies).
/// </summary>
/// <param name="VehicleOf">Finds the vehicle of what it changes, whoever may see it (a parked change is filed with its vehicle, also when it was
/// parked because the access is gone); needed when <paramref name="VehicleIdHint"/> is not known from the change itself.</param>
public sealed record SyncChangeRequest(
    Guid Id, SyncChangeKind Kind, Guid? TargetId, Guid? VehicleIdHint, int? ExpectedVersion, string Payload, Func<CancellationToken, Task<AppliedChange>> Apply,
    Func<CancellationToken, Task<Guid?>>? VehicleOf = null);

public sealed record SyncChangeResult(Guid Id, SyncChangeStatus Status, Guid? EntityId, int? Version, string? ReasonKey, IReadOnlyDictionary<string, string> ReasonArgs)
{
    public static SyncChangeResult From(SyncChange c) => new(c.Id, c.Status, c.ResultId, c.ResultVersion, c.ReasonKey, c.ReasonArgs);
}

public sealed record SyncOutcome(IReadOnlyList<SyncChangeResult> Results, int Applied, int Parked);

public interface ISyncChangeRepository
{
    Task<IReadOnlyList<SyncChange>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<SyncChange?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>The parked changes of the vehicles in the scope (live vehicles only), and those the submitter sent, newest first.</summary>
    Task<IReadOnlyList<SyncChange>> ListParkedAsync(OwnerScope scope, Guid submitterId, CancellationToken ct);

    /// <summary>How many parked changes each of the vehicles has.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountParkedAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct);

    /// <summary>Records a change; false when a change with its id is recorded already (the same batch on another server at the same moment).</summary>
    Task<bool> AddAsync(SyncChange change, CancellationToken ct);

    /// <summary>
    /// Saves what was decided about a parked change only if it is still parked (two people may decide at once); false when someone else was
    /// first.
    /// </summary>
    Task<bool> SettleParkedAsync(SyncChange change, CancellationToken ct);

    /// <summary>Removes the applied (and discarded) rows received before the moment; parked ones stay.</summary>
    Task<int> PurgeResolvedAsync(DateTimeOffset before, CancellationToken ct);
}

/// <summary>
/// Applies the changes a device kept while the server was out of reach (<c>syncChanges</c>), in the order they were made, each through the
/// same service as its single mutation, with the version it was made from: so every rule applies, and a change made from an old version is
/// noticed. What the server cannot apply is parked (with the reason and the change), never lost; what it applied is recorded, so a batch
/// sent again (an answer that never arrived) is answered with what was recorded and applied once. One batch of a user at a time.
/// </summary>
public sealed class SyncService(
    ISyncChangeRepository ledger, IVehicleRepository vehicles, AccessService access, IOptions<SyncOptions> options, TimeProvider clock, ILogger<SyncService> logger)
{
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<SyncOutcome> SyncAsync(IReadOnlyList<SyncChangeRequest> changes, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        if (changes.Count is 0 or > SyncChange.MaxBatch)
            throw new DomainException("sync.tooManyChanges", $"A batch holds 1 to {SyncChange.MaxBatch} changes.", new { Max = SyncChange.MaxBatch });
        if (changes.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new DomainException("sync.duplicateChange", "A change is in the batch twice.", new { Id = twice.Key });
        if (changes.FirstOrDefault(c => c.Payload.Length > SyncChange.MaxPayloadLength) is { } large)
            throw new DomainException("sync.payloadTooLarge", $"A change may hold at most {SyncChange.MaxPayloadLength} characters.", new { large.Id, Max = SyncChange.MaxPayloadLength });

        var gate = Locks[(uint)me.Id.GetHashCode() % Locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            var known = (await ledger.FindManyAsync([.. changes.Select(c => c.Id)], ct)).ToDictionary(c => c.Id);
            if (known.Values.FirstOrDefault(c => c.SubmittedById != me.Id) is { } taken)
                throw new DomainException("sync.idTaken", $"The id {taken.Id} is already in use.", new { taken.Id });

            var results = new List<SyncChangeResult>(changes.Count);
            var replayed = 0;
            foreach (var change in changes)
            {
                if (known.TryGetValue(change.Id, out var recorded))
                {
                    results.Add(SyncChangeResult.From(recorded)); // sent again: what happened the first time
                    replayed++;
                    continue;
                }
                var row = await ApplyAsync(change, me.Id, ct);
                // Recorded whatever the request does now: what was applied must be in the ledger, or the same change sent again (the device
                // never heard back) would be applied a second time, or parked as a version mismatch with itself.
                if (!await ledger.AddAsync(row, CancellationToken.None))
                    row = (await ledger.FindManyAsync([row.Id], CancellationToken.None)).Single(); // another server took the same batch first
                results.Add(SyncChangeResult.From(row));
                logger.LogDebug("Change {ChangeId} ({Kind}) of vehicle {VehicleId}: {Status} {Key}", row.Id, row.Kind, row.VehicleId, row.Status, row.ReasonKey);
            }

            var purged = await ledger.PurgeResolvedAsync(clock.GetUtcNow() - TimeSpan.FromDays(options.Value.RetentionDays), ct);
            var applied = results.Count(r => r.Status == SyncChangeStatus.Applied);
            var parked = results.Count(r => r.Status == SyncChangeStatus.Parked);
            logger.LogInformation("User {UserId} synced {Count} changes: {Applied} applied, {Parked} parked, {Replayed} sent again, {Purged} old records removed",
                me.Id, changes.Count, applied, parked, replayed, purged);
            return new SyncOutcome(results, applied, parked);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The parked changes the user may see: those of every vehicle whose logs they may view (everyone who works with the vehicle decides
    /// about them), and those they sent themselves (a vehicle they added, which the server never took, has none). An administrator sees
    /// every vehicle's here, as in every list; the notifications only go to those who work with the vehicle themselves
    /// (<see cref="Notifications.SyncNotificationSync"/>), deliberately.
    /// </summary>
    public async Task<IReadOnlyList<SyncChange>> ListParkedAsync(CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        return await ledger.ListParkedAsync(await access.LogScopeAsync(AccessLevel.View, ct), me.Id, ct);
    }

    /// <summary>For vehicles the caller already got through an access check (a resolver of the vehicle).</summary>
    public Task<IReadOnlyDictionary<Guid, int>> CountParkedAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct) => ledger.CountParkedAsync(vehicleIds, ct);

    /// <summary>Whoever sent it, or may change the vehicle's logs, decides about a parked change.</summary>
    public async Task<bool> CanResolveAsync(SyncChange change, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        if (change.SubmittedById == me.Id) return true;
        return change.VehicleId is { } id && await vehicles.FindAsync(id, ct) is { } vehicle && await access.LogLevelAsync(vehicle, ct) >= AccessLevel.Edit;
    }

    /// <summary>
    /// A person decides about a parked change: discard it (it is never applied), or apply it anyway, as they would online (every rule and
    /// access check; the version it was made from no longer counts: they saw what is there now). <paramref name="apply"/> applies the
    /// stored change, or the edited one in <paramref name="replacement"/> (stored first, so it is kept if it is refused again). A refusal
    /// keeps it parked with the new reason, and is thrown so the person sees it.
    /// </summary>
    public async Task<SyncChange> ResolveAsync(Guid id, bool discard, Func<CancellationToken, Task<AppliedChange>>? apply, string? replacement, CancellationToken ct)
    {
        var me = await access.RequirePrincipalAsync(ct);
        var change = await ledger.FindAsync(id, ct);
        if (change is null || !await VisibleAsync(change, me.Id, ct)) throw new NotFoundException("sync.notFound", "Change not found.", new { Id = id });
        if (change.Status != SyncChangeStatus.Parked) throw NotParked();
        if (!await CanResolveAsync(change, ct)) throw new ForbiddenException("sync.cannotResolve", "Only whoever sent it, or may change the vehicle's logs, decides about it.");

        var now = clock.GetUtcNow();
        if (discard)
        {
            change.MarkDiscarded(me.Id, now);
            if (!await ledger.SettleParkedAsync(change, ct)) throw NotParked();
            logger.LogInformation("User {UserId} discarded parked change {ChangeId} of vehicle {VehicleId}", me.Id, change.Id, change.VehicleId);
            return change;
        }
        if (replacement is not null) change.Replace(replacement);
        try
        {
            var applied = await apply!(ct);
            change.MarkApplied(me.Id, now, applied.EntityId, applied.Version);
            if (!await ledger.SettleParkedAsync(change, ct)) throw NotParked(); // someone else decided meanwhile (applying it again changed nothing new)
            logger.LogInformation("User {UserId} applied parked change {ChangeId} of vehicle {VehicleId}", me.Id, change.Id, change.VehicleId);
            return change;
        }
        catch (KeyedException refused)
        {
            change.ParkAgain(refused.Key, refused.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? ""));
            if (!await ledger.SettleParkedAsync(change, ct)) throw NotParked();
            logger.LogDebug("Parked change {ChangeId} was refused again: {Key}", change.Id, refused.Key);
            throw;
        }
    }

    private static DomainException NotParked() => new("sync.notParked", "This change is not parked: someone has decided about it already.");

    private async Task<bool> VisibleAsync(SyncChange change, Guid me, CancellationToken ct) =>
        change.SubmittedById == me
        || (change.VehicleId is { } id && await vehicles.FindAsync(id, ct) is { } vehicle && await access.LogLevelAsync(vehicle, ct) >= AccessLevel.View);

    /// <summary>Applies one change; a refusal of the server's (a keyed error) parks it with the reason, anything else is unexpected and stops the batch.</summary>
    private async Task<SyncChange> ApplyAsync(SyncChangeRequest change, Guid submitter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        try
        {
            var applied = await change.Apply(ct);
            var (vehicleId, ownerId) = await FiledUnderAsync(applied.VehicleId ?? change.VehicleIdHint, submitter, ct);
            return SyncChange.Applied(change.Id, ownerId, submitter, vehicleId, applied.EntityId ?? change.TargetId, change.Kind,
                change.ExpectedVersion, change.Payload, now, applied.EntityId, applied.Version);
        }
        catch (KeyedException refused)
        {
            var args = refused.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? "");
            var (vehicleId, ownerId) = await FiledUnderAsync(change.VehicleIdHint ?? (change.VehicleOf is { } find ? await find(ct) : null), submitter, ct);
            return SyncChange.Parked(change.Id, ownerId, submitter, vehicleId, change.TargetId, change.Kind,
                change.ExpectedVersion, change.Payload, now, refused.Key, args);
        }
    }

    /// <summary>
    /// The vehicle a change is filed under and its owner (whoever may see the vehicle sees the change). A vehicle that does not exist (purged
    /// meanwhile, or the one an add the server refused would have added) files it with the sender alone: the ledger's vehicle has a foreign
    /// key, and a batch must never fail on a change it parks.
    /// </summary>
    private async Task<(Guid? VehicleId, Guid OwnerId)> FiledUnderAsync(Guid? vehicleId, Guid submitter, CancellationToken ct) =>
        vehicleId is { } id && await vehicles.FindIncludingDeletedAsync(id, ct) is { } vehicle ? (vehicle.Id, vehicle.OwnerId) : (null, submitter);
}
