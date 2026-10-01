using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Domain.Access;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class ResourceGrantRepository(IDbContextFactory<AppDbContext> dbFactory) : IResourceGrantRepository
{
    public async Task<IReadOnlyList<ResourceGrant>> ListForResourceAsync(ResourceType type, Guid resourceId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ResourceGrants.AsNoTracking().Where(g => g.ResourceType == type && g.ResourceId == resourceId).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ResourceGrant>> ListForGranteeAsync(Guid granteeId, ResourceType type, GrantedFeature feature, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ResourceGrants.AsNoTracking().Where(g => g.GranteeId == granteeId && g.ResourceType == type && g.Feature == feature).ToListAsync(ct);
    }

    public async Task<ResourceGrant?> FindAsync(ResourceType type, Guid resourceId, Guid granteeId, GrantedFeature feature, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ResourceGrants.AsNoTracking().FirstOrDefaultAsync(
            g => g.ResourceType == type && g.ResourceId == resourceId && g.GranteeId == granteeId && g.Feature == feature, ct);
    }

    public async Task AddAsync(ResourceGrant grant, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ResourceGrants.Add(grant);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ResourceGrant grant, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ResourceGrants.Update(grant);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(ResourceGrant grant, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ResourceGrants.Remove(grant);
        await db.SaveChangesAsync(ct);
    }
}
