using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations before the host starts accepting requests.</summary>
internal sealed class DatabaseMigrator(IDbContextFactory<AppDbContext> dbFactory, ILogger<DatabaseMigrator> logger) : IHostedService
{
    /// <summary>The migration that added the stored consumption: logs that already existed get theirs calculated once, right after it ran.</summary>
    private const string ConsumptionMigration = "_AddRefuelingConsumption";

    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var backfill = pending.Any(m => m.EndsWith(ConsumptionMigration, StringComparison.Ordinal));

        // The one line that tells an operator the database was changed by this start, and by what.
        if (pending.Count > 0) logger.LogInformation("Applying {Count} database migrations ({Provider}): {Migrations}", pending.Count, db.Database.ProviderName, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        if (pending.Count > 0) logger.LogInformation("Applied {Count} database migrations", pending.Count);
        else logger.LogInformation("The database schema is up to date ({Provider})", db.Database.ProviderName);

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
        if (vehicleIds.Count > 0) logger.LogInformation("Calculated the fuel consumption of the existing refuelings of {Vehicles} vehicles", vehicleIds.Count); // a new installation has none
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
