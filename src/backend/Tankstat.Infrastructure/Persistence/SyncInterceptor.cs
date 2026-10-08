using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tankstat.Domain.Sync;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>
/// Keeps what devices download for offline use current, in one place: every save of an <see cref="ISynced"/> entity sets its
/// <c>UpdatedAt</c> (the repositories save detached graphs with <c>Update</c>, so any save counts), and removing one for good leaves a
/// <see cref="Tombstone"/> in the same save. Updates and deletes are conditional on the version the entity was loaded with (a concurrency
/// token), so two changes made from the same version cannot both be saved. Writes that bypass the change tracker (<c>ExecuteUpdate</c>,
/// <c>ExecuteDelete</c>) set the column or write the tombstones themselves.
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

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>What was saved is what is stored now: an entity saved again (the same instance) starts from the version it has.</summary>
    private static void Saved(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries<ISynced>()) entry.Entity.Saved();
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
                case EntityState.Added:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    // A conditional write: only if the stored row still has the version this entity was loaded with (the repositories
                    // save detached entities, so EF's own original value would be the new one).
                    entry.Property(e => e.Version).OriginalValue = entry.Entity.SavedVersion;
                    break;
                case EntityState.Deleted:
                    entry.Property(e => e.Version).OriginalValue = entry.Entity.SavedVersion;
                    context.Add(Tombstone.For(entry.Entity, now));
                    break;
            }
        }
    }
}
