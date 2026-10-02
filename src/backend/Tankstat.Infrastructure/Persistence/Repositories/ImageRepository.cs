using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class ImageRepository(IDbContextFactory<AppDbContext> dbFactory) : IImageRepository
{
    public async Task AddAsync(StoredImage image, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Images.Add(image);
        await db.SaveChangesAsync(ct);
    }

    public async Task<StoredImage?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Images.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task RemoveFolderAsync(string folder, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var below = folder + "/";
        await db.Images.Where(i => i.Folder == folder || (i.Folder != null && i.Folder.StartsWith(below))).ExecuteDeleteAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Images.FirstOrDefaultAsync(i => i.Id == id, ct) is { } image)
        {
            db.Images.Remove(image);
            await db.SaveChangesAsync(ct);
        }
    }
}
