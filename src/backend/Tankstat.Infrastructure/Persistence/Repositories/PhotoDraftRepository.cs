using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Photos;
using Tankstat.Domain.Photos;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class PhotoDraftRepository(IDbContextFactory<AppDbContext> dbFactory) : IPhotoDraftRepository
{
    public async Task<PhotoDraft?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<IReadOnlyList<PhotoDraft>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoDrafts.AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
    }

    public async Task<int> CountAsync(Guid vehicleId, Guid createdById, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoDrafts.CountAsync(d => d.VehicleId == vehicleId && d.CreatedById == createdById, ct);
    }

    public async Task<IReadOnlyList<PhotoDraft>> ListCreatedBeforeAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoDrafts.AsNoTracking().Where(d => d.CreatedAt < before).ToListAsync(ct);
    }

    public async Task AddAsync(PhotoDraft draft, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PhotoDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.PhotoDrafts.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct);
    }
}
