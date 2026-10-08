using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
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

    Task AddAsync(SyncChange change, CancellationToken ct);

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
                await ledger.AddAsync(row, ct);
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

    /// <summary>Applies one change; a refusal of the server's (a keyed error) parks it with the reason, anything else is unexpected and stops the batch.</summary>
    private async Task<SyncChange> ApplyAsync(SyncChangeRequest change, Guid submitter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        try
        {
            var applied = await change.Apply(ct);
            var vehicleId = applied.VehicleId ?? change.VehicleIdHint;
            return SyncChange.Applied(change.Id, await OwnerOfAsync(vehicleId, submitter, ct), submitter, vehicleId, applied.EntityId ?? change.TargetId, change.Kind,
                change.ExpectedVersion, change.Payload, now, applied.EntityId, applied.Version);
        }
        catch (KeyedException refused)
        {
            var args = refused.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? "");
            var vehicleId = change.VehicleIdHint ?? (change.VehicleOf is { } find ? await find(ct) : null);
            return SyncChange.Parked(change.Id, await OwnerOfAsync(vehicleId, submitter, ct), submitter, vehicleId, change.TargetId, change.Kind,
                change.ExpectedVersion, change.Payload, now, refused.Key, args);
        }
    }

    /// <summary>The vehicle's owner, whoever may see it (the ledger row is filed with the vehicle); the submitter when it is unknown.</summary>
    private async Task<Guid> OwnerOfAsync(Guid? vehicleId, Guid submitter, CancellationToken ct) =>
        vehicleId is { } id && await vehicles.FindIncludingDeletedAsync(id, ct) is { } vehicle ? vehicle.OwnerId : submitter;
}
