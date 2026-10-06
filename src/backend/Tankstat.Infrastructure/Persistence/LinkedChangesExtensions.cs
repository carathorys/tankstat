using Microsoft.EntityFrameworkCore;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence;

internal static class LinkedChangesExtensions
{
    /// <summary>
    /// Saves a log loaded without tracking, with the reading and cost it created (new) and let go of (deleted). The let-go rows are deleted
    /// in a second save of the same transaction: the context never saw the log point at them, so in one save it could delete a row before
    /// the log stops referring to it, which the database refuses.
    /// </summary>
    public static async Task SaveLogAsync<TLog>(this AppDbContext db, TLog log, LinkedChanges changes, CancellationToken ct) where TLog : class
    {
        await using var tx = changes.RemovedReading is null && changes.RemovedCost is null ? null : await db.Database.BeginTransactionAsync(ct);
        db.Update(log); // marks the loaded graph (cost, reading) as modified; new and let-go rows are said so explicitly
        if (changes.CreatedReading is { } createdReading) db.Entry(createdReading).State = EntityState.Added;
        if (changes.CreatedCost is { } createdCost) db.Entry(createdCost).State = EntityState.Added;
        await db.SaveChangesAsync(ct);
        if (tx is null) return;
        if (changes.RemovedReading is { } removedReading) db.Entry(removedReading).State = EntityState.Deleted;
        if (changes.RemovedCost is { } removedCost) db.Entry(removedCost).State = EntityState.Deleted;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
