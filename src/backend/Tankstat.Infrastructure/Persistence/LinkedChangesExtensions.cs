using Microsoft.EntityFrameworkCore;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence;

internal static class LinkedChangesExtensions
{
    /// <summary>Ids are made by the domain, so a reading or cost a log created is said to be new, and one it let go of to be deleted.</summary>
    public static void Apply(this AppDbContext db, LinkedChanges changes)
    {
        if (changes.CreatedReading is { } createdReading) db.Entry(createdReading).State = EntityState.Added;
        if (changes.RemovedReading is { } removedReading) db.Entry(removedReading).State = EntityState.Deleted;
        if (changes.CreatedCost is { } createdCost) db.Entry(createdCost).State = EntityState.Added;
        if (changes.RemovedCost is { } removedCost) db.Entry(removedCost).State = EntityState.Deleted;
    }
}
