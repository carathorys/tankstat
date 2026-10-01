using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations before the host starts accepting requests.</summary>
internal sealed class DatabaseMigrator(IDbContextFactory<AppDbContext> dbFactory) : IHostedService
{
    /// <summary>The migration that added the stored consumption: logs that already existed get theirs calculated once, right after it ran.</summary>
    private const string ConsumptionMigration = "_AddRefuelingConsumption";

    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var backfill = (await db.Database.GetPendingMigrationsAsync(ct)).Any(m => m.EndsWith(ConsumptionMigration, StringComparison.Ordinal));
        await db.Database.MigrateAsync(ct);
        if (backfill) await BackfillConsumptionAsync(ct);
    }

    internal async Task BackfillConsumptionAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var vehicleIds = await db.Refuelings.Select(r => r.VehicleId).Distinct().ToListAsync(ct);
        foreach (var id in vehicleIds)
        {
            var logs = await db.Refuelings.Include(r => r.OdometerReading).Where(r => r.VehicleId == id).ToListAsync(ct);
            ConsumptionCalculator.Apply(logs);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
