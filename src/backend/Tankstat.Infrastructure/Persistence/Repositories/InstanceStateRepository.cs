using Microsoft.EntityFrameworkCore;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain.Instance;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class InstanceStateRepository(IDbContextFactory<AppDbContext> dbFactory) : IInstanceStateRepository
{
    public async Task<InstanceState?> GetAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.InstanceState.AsNoTracking().FirstOrDefaultAsync(s => s.Id == InstanceState.SingletonId, ct);
    }

    /// <summary>Two instances starting at once on one database both see no row; the one that loses the insert becomes an update.</summary>
    public async Task SaveAsync(InstanceState state, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isNew = !await db.InstanceState.AnyAsync(s => s.Id == state.Id, ct);
        db.Entry(state).State = isNew ? EntityState.Added : EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNew)
        {
            db.ChangeTracker.Clear();
            db.Entry(state).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);
        }
    }
}
