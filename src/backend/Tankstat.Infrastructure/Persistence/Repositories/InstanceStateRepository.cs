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

    /// <summary>
    /// Two instances starting at once on one database both see no row; the one that loses the insert becomes an update. An update leaves the
    /// setup columns as they are: they are <c>SetupStore</c>'s, changed only by its conditional updates, and written back as this row was
    /// read they would undo a completion another server made meanwhile.
    /// </summary>
    public async Task SaveAsync(InstanceState state, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isNew = !await db.InstanceState.AnyAsync(s => s.Id == state.Id, ct);
        if (isNew) db.Entry(state).State = EntityState.Added;
        else Update(db, state);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNew)
        {
            db.ChangeTracker.Clear();
            Update(db, state);
            await db.SaveChangesAsync(ct);
        }
    }

    private static void Update(AppDbContext db, InstanceState state)
    {
        var entry = db.Entry(state);
        entry.State = EntityState.Modified;
        entry.Property(s => s.SetupPendingSince).IsModified = false;
        entry.Property(s => s.SetupCompletedAt).IsModified = false;
        entry.Property(s => s.SetupCompletedVia).IsModified = false;
    }
}
