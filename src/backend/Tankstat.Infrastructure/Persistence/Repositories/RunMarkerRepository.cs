using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Runs;
using Tankstat.Domain.Runs;
using Tankstat.Infrastructure.Persistence.Runs;

namespace Tankstat.Infrastructure.Persistence.Repositories;

/// <summary>Every context here is leased outside a run's transaction (<see cref="RunDbContextFactory.CreateOutside"/>): a mark stays when its run rolls back.</summary>
internal sealed class RunMarkerRepository(RunDbContextFactory dbFactory) : IRunMarkerRepository
{
    public async Task AddAsync(RunMarker marker, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        db.RunMarkers.Add(marker);
        await db.SaveChangesAsync(ct);
    }

    public async Task EndAsync(Guid id, RunEnd end, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        await db.RunMarkers.Where(m => m.Id == id && m.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.EndedAt, at).SetProperty(m => m.End, end), ct);
    }

    public async Task<RunMarker?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.AsNoTracking().Include(m => m.Vehicles).FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<bool> AnyOpenAsync(DateTimeOffset startedAfter, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.AnyAsync(m => m.EndedAt == null && m.StartedAt > startedAfter, ct);
    }

    public async Task<bool> HoldsAsync(Guid vehicleId, DateTimeOffset startedAfter, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.AnyAsync(m => m.EndedAt == null && m.StartedAt > startedAfter && m.Vehicles.Any(v => v.VehicleId == vehicleId), ct);
    }

    public async Task<IReadOnlyList<RunMarker>> ListOpenAsync(DateTimeOffset? startedBefore, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.AsNoTracking().Include(m => m.Vehicles)
            .Where(m => m.EndedAt == null && (startedBefore == null || m.StartedAt < startedBefore))
            .OrderBy(m => m.StartedAt).ThenBy(m => m.Id).ToListAsync(ct);
    }

    public async Task<bool> RequestCancelAsync(Guid id, Guid by, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.Where(m => m.Id == id && m.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancelRequestedAt, m => m.CancelRequestedAt ?? at).SetProperty(m => m.CancelRequestedById, m => m.CancelRequestedById ?? by), ct) > 0;
    }

    public async Task<bool> IsCancelRequestedAsync(Guid id, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.AnyAsync(m => m.Id == id && m.CancelRequestedAt != null, ct);
    }

    public async Task<int> PruneAsync(DateTimeOffset endedBefore, CancellationToken ct)
    {
        await using var db = dbFactory.CreateOutside();
        return await db.RunMarkers.Where(m => m.EndedAt != null && m.EndedAt < endedBefore).ExecuteDeleteAsync(ct);
    }
}
