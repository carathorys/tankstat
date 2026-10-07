using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tankstat.Domain.Sync;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>
/// Keeps what devices download for offline use current, in one place: every save of an <see cref="ISynced"/> entity sets its
/// <c>UpdatedAt</c> (the repositories save detached graphs with <c>Update</c>, so any save counts), and removing one for good leaves a
/// <see cref="Tombstone"/> in the same save. Writes that bypass the change tracker (<c>ExecuteUpdate</c>, <c>ExecuteDelete</c>) set the
/// column or write the tombstones themselves.
/// </summary>
public sealed class SyncInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = clock.GetUtcNow();
        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries<ISynced>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added or EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;
                case EntityState.Deleted:
                    context.Add(Tombstone.For(entry.Entity, now));
                    break;
            }
        }
    }
}
