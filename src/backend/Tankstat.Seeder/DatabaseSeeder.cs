using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Seeder;

public sealed record SeedResult(int Vehicles, int Trashed, int Refuelings);

/// <summary>Deletes, creates and migrates the database, then inserts the generated data in batches.</summary>
public sealed class DatabaseSeeder(IDbContextFactory<AppDbContext> dbFactory, DataGenerator generator)
{
    private const int BatchVehicles = 500;

    public async Task<SeedResult> RecreateAndSeedAsync(SeedOptions options, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await db.Database.EnsureDeletedAsync(ct);
            SqliteConnection.ClearAllPools(); // release the file so it can be recreated at once
            await db.Database.MigrateAsync(ct);
        }

        int vehicles = 0, trashed = 0, refuelings = 0;
        foreach (var batch in generator.Generate(options).Chunk(BatchVehicles))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.Vehicles.AddRange(batch.Select(b => b.Vehicle));
            db.Refuelings.AddRange(batch.SelectMany(b => b.Refuelings));
            await db.SaveChangesAsync(ct);

            vehicles += batch.Count(b => !b.Vehicle.IsDeleted);
            trashed += batch.Count(b => b.Vehicle.IsDeleted);
            refuelings += batch.Sum(b => b.Refuelings.Count);
        }
        return new SeedResult(vehicles, trashed, refuelings);
    }
}
