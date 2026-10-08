using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Application.Sync;
using Tankstat.Domain.Sync;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class SyncChangeRepository(IDbContextFactory<AppDbContext> dbFactory) : ISyncChangeRepository
{
    public async Task<IReadOnlyList<SyncChange>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SyncChanges.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
    }

    public async Task<SyncChange?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SyncChanges.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<SyncChange>> ListParkedAsync(OwnerScope scope, Guid submitterId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var parked = db.SyncChanges.AsNoTracking().Where(c => c.Status == SyncChangeStatus.Parked);
        // The vehicles' query filter leaves out the trashed ones: their changes wait until the vehicle is back (or go with it).
        var ofVehicles = parked.Where(c => c.VehicleId != null && db.Vehicles.Any(v => v.Id == c.VehicleId)).InScope(scope, c => c.VehicleId!.Value);
        var mine = parked.Where(c => c.SubmittedById == submitterId);
        var rows = await ofVehicles.Union(mine).ToListAsync(ct);
        return rows.OrderByDescending(c => c.ReceivedAt).ThenBy(c => c.Id).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountParkedAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return new Dictionary<Guid, int>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SyncChanges.Where(c => c.Status == SyncChangeStatus.Parked && c.VehicleId != null && vehicleIds.Contains(c.VehicleId.Value))
            .GroupBy(c => c.VehicleId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public async Task<bool> SettleParkedAsync(SyncChange change, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // A conditional write: of two people deciding at once, only the first one's decision is saved.
        var saved = await db.SyncChanges.Where(c => c.Id == change.Id && c.Status == SyncChangeStatus.Parked).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Status, change.Status)
            .SetProperty(c => c.Payload, change.Payload)
            .SetProperty(c => c.ReasonKey, change.ReasonKey)
            .SetProperty(c => c.ReasonArgs, change.ReasonArgs)
            .SetProperty(c => c.ResultId, change.ResultId)
            .SetProperty(c => c.ResultVersion, change.ResultVersion)
            .SetProperty(c => c.ResolvedById, change.ResolvedById)
            .SetProperty(c => c.ResolvedAt, change.ResolvedAt), ct);
        return saved == 1;
    }

    public Task<bool> AddAsync(SyncChange change, CancellationToken ct) => dbFactory.AddOnceAsync(change, change.Id, ct);

    public async Task<int> PurgeResolvedAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SyncChanges.Where(c => c.Status != SyncChangeStatus.Parked && c.ReceivedAt < before).ExecuteDeleteAsync(ct);
    }
}
