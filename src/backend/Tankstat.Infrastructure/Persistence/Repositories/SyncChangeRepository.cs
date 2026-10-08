using Microsoft.EntityFrameworkCore;
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

    public Task<bool> AddAsync(SyncChange change, CancellationToken ct) => dbFactory.AddOnceAsync(change, change.Id, ct);

    public async Task<int> PurgeResolvedAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SyncChanges.Where(c => c.Status != SyncChangeStatus.Parked && c.ReceivedAt < before).ExecuteDeleteAsync(ct);
    }
}
