using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class PhotoReadingRepository(IDbContextFactory<AppDbContext> dbFactory) : IPhotoReadingRepository
{
    public async Task AddAsync(PhotoReading reading, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PhotoReadings.Add(reading);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PhotoReading>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoReadings.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoReadings
            .Where(r => r.Status == ReadingStatus.Queued && r.DueAt <= now)
            .OrderBy(r => r.DueAt).ThenBy(r => r.Id)
            .Select(r => r.Id)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task<PhotoReading?> ClaimAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The same change as PhotoReading.Claim, made only if the row is still queued: of two workers, one updates it and one finds nothing.
        var claimed = await db.PhotoReadings
            .Where(r => r.Id == id && r.Status == ReadingStatus.Queued && r.DueAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReadingStatus.Reading)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                .SetProperty(r => r.ClaimedAt, now), ct);
        return claimed == 0 ? null : await db.PhotoReadings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task SaveAsync(PhotoReading reading, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PhotoReadings.Update(reading);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The picture (and with it the reading) was removed while it was being read: nothing left to save.
        }
    }

    public async Task<IReadOnlyList<PhotoReading>> ListStaleAsync(DateTimeOffset claimedBefore, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PhotoReadings.AsNoTracking()
            .Where(r => r.Status == ReadingStatus.Reading && r.ClaimedAt < claimedBefore)
            .ToListAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.PhotoReadings.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
    }
}
