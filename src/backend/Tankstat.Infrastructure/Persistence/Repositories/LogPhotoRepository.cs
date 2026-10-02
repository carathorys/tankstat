using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Photos;
using Tankstat.Domain.Photos;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class LogPhotoRepository(IDbContextFactory<AppDbContext> dbFactory) : ILogPhotoRepository
{
    public async Task<IReadOnlyList<LogPhoto>> ListForLogAsync(LogType logType, Guid logId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LogPhotos.AsNoTracking().Where(p => p.LogType == logType && p.LogId == logId).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LogPhoto>> ListForLogsAsync(LogType logType, IReadOnlyCollection<Guid> logIds, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LogPhotos.AsNoTracking().Where(p => p.LogType == logType && logIds.Contains(p.LogId)).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
    }

    public async Task<int> CountForLogAsync(LogType logType, Guid logId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LogPhotos.CountAsync(p => p.LogType == logType && p.LogId == logId, ct);
    }

    public async Task<LogPhoto?> FindByImageAsync(Guid imageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LogPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.ImageId == imageId, ct);
    }

    public async Task AddAsync(LogPhoto photo, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.LogPhotos.Add(photo);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(LogPhoto photo, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.LogPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
    }
}
