using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations before the host starts accepting requests, and what has to follow a migration that ran (the stored
/// consumption of logs that existed before it). At start, a database that holds data and has migrations pending is backed up first, always
/// (a safety net, kept apart by retention like a restore's): when that backup cannot be written nothing is migrated, and the start stops
/// with the reason, since an upgrade is the change a way back is needed for. A new, empty database has nothing to back up, and a start with
/// a restore waiting writes none either: the data is about to be replaced by a backup, and the restore writes its own safety net of what
/// it replaces. A restore of a backup made at an older migration brings the restored data up to date through <see cref="MigrateAsync"/>
/// too, with no backup of its own, for the same reason.
/// </summary>
internal sealed class DatabaseMigrator(IDbContextFactory<AppDbContext> dbFactory, IBackupWriter backups, IBackupStore store, ILogger<DatabaseMigrator> logger) : IHostedService
{
    /// <summary>The migration that added the stored consumption: logs that already existed get theirs calculated once, right after it ran.</summary>
    private const string ConsumptionMigration = "_AddRefuelingConsumption";

    public Task StartAsync(CancellationToken ct) => MigrateAsync(ct, backupFirst: true);

    /// <summary>Every pending migration (after a backup of the database as it is, with <paramref name="backupFirst"/>), then what has to follow the ones that ran.</summary>
    public async Task MigrateAsync(CancellationToken ct, bool backupFirst = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var backfill = pending.Any(m => m.EndsWith(ConsumptionMigration, StringComparison.Ordinal));
        if (backupFirst && pending.Count > 0 && (await db.Database.GetAppliedMigrationsAsync(ct)).Any())
        {
            if (store.RestorePending) logger.LogInformation("A restore waits, which writes its own safety net: no backup before the database migrations");
            else await BackUpAsync(pending.Count, ct);
        }

        // The one line that tells an operator the database was changed by this start, and by what.
        if (pending.Count > 0) logger.LogInformation("Applying {Count} database migrations ({Provider}): {Migrations}", pending.Count, db.Database.ProviderName, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        if (pending.Count > 0) logger.LogInformation("Applied {Count} database migrations", pending.Count);
        else logger.LogInformation("The database schema is up to date ({Provider})", db.Database.ProviderName);

        if (backfill) await BackfillConsumptionAsync(ct);
    }

    /// <summary>The backup before the migrations; when it cannot be written, nothing is migrated and the start stops, saying why.</summary>
    private async Task BackUpAsync(int pending, CancellationToken ct)
    {
        logger.LogInformation("Backing up the database before {Count} database migrations are applied", pending);
        try
        {
            await backups.WriteAsync(new BackupRequest(SafetyNet: true), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "The backup before the database migrations could not be written, so the database was not migrated. Fix the cause (the backups folder, its disk, the Backups settings) and start again.", e);
        }
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
