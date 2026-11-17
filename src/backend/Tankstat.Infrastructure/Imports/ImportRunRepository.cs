using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Imports;
using Tankstat.Domain.Imports;
using Tankstat.Infrastructure.Persistence;
using Tankstat.Infrastructure.Persistence.Runs;

namespace Tankstat.Infrastructure.Imports;

/// <summary>
/// The runs' rows. Only <see cref="CompleteAsync"/> is part of the run's transaction (it is called inside the run, through the factory
/// every repository takes): the report commits with the cars or not at all. Everything else goes around a run (<c>CreateOutside</c>),
/// so a run's progress and its end are written whatever the run itself does.
/// </summary>
internal sealed class ImportRunRepository(RunDbContextFactory outside, IDbContextFactory<AppDbContext> dbFactory) : IImportRunRepository
{
    public async Task AddAsync(ImportRun run, CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ImportRun?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        return await db.ImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<ImportRun>> ListActiveAsync(CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        return await db.ImportRuns.AsNoTracking().Where(r => r.State == ImportRunState.Waiting || r.State == ImportRunState.Running)
            .OrderBy(r => r.QueuedAt).ThenBy(r => r.Id).ToListAsync(ct);
    }

    public async Task<ImportRun?> LatestOfAsync(Guid userId, CancellationToken ct, ImportRunKind? kind = null)
    {
        await using var db = outside.CreateOutside();
        var mine = db.ImportRuns.AsNoTracking().Where(r => r.StartedById == userId && (kind == null || r.Kind == kind));
        return await mine.Where(r => r.State == ImportRunState.Waiting || r.State == ImportRunState.Running).OrderBy(r => r.QueuedAt).FirstOrDefaultAsync(ct)
            ?? await mine.OrderByDescending(r => r.QueuedAt).ThenBy(r => r.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ClaimAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        return await db.ImportRuns
            .Where(r => r.Id == id && r.State == ImportRunState.Waiting
                && !db.ImportRuns.Any(o => o.State == ImportRunState.Running)
                && !db.ImportRuns.Any(o => o.State == ImportRunState.Waiting && o.Id != id && o.QueuedAt < r.QueuedAt))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, ImportRunState.Running).SetProperty(r => r.StartedAt, now).SetProperty(r => r.SeenAt, now), ct) > 0;
    }

    public async Task CompleteAsync(Guid id, string report, int cars, int entries, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct); // inside the run: its transaction's
        var run = await db.ImportRuns.FirstAsync(r => r.Id == id, ct);
        // Ended from outside meanwhile (its starter was told so): what the row says stands, and the run rolls back with this.
        if (run.State != ImportRunState.Running) throw new InvalidOperationException("The run was ended while it ran.");
        run.Complete(report.Length > ImportRun.MaxJsonLength ? "{\"cars\":[]}" : report, cars, entries, now);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> FailAsync(Guid id, ImportRunFailure failure, Guid? cancelledById, DateTimeOffset now, CancellationToken ct, bool waitingOnly = false)
    {
        await using var db = outside.CreateOutside();
        return await db.ImportRuns.Where(r => r.Id == id && (r.State == ImportRunState.Waiting || (!waitingOnly && r.State == ImportRunState.Running)))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, ImportRunState.NotFinished).SetProperty(r => r.Failure, failure)
                .SetProperty(r => r.CancelledById, cancelledById).SetProperty(r => r.EndedAt, now), ct) > 0;
    }

    public async Task ProgressAsync(Guid id, int doneCars, int doneEntries, CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        await db.ImportRuns.Where(r => r.Id == id && r.State == ImportRunState.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DoneCars, doneCars).SetProperty(r => r.DoneEntries, doneEntries), ct);
    }

    public async Task SeenAsync(string server, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        await db.ImportRuns.Where(r => r.Server == server && (r.State == ImportRunState.Waiting || r.State == ImportRunState.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SeenAt, now), ct);
    }

    public async Task<int> PruneAsync(DateTimeOffset endedBefore, CancellationToken ct)
    {
        await using var db = outside.CreateOutside();
        return await db.ImportRuns.Where(r => r.EndedAt != null && r.EndedAt < endedBefore).ExecuteDeleteAsync(ct);
    }
}

/// <summary>The cars that are here, read for the import, through the factory every repository takes: inside a run the reads see what the run has saved.</summary>
internal sealed class ImportTargetReader(IDbContextFactory<AppDbContext> dbFactory) : IImportTargetReader
{
    public async Task<IReadOnlyList<Tankstat.Domain.Vehicles.Vehicle>> OwnedAsync(Guid ownerId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Where(v => v.OwnerId == ownerId).OrderBy(v => v.Name).ThenBy(v => v.Id).ToListAsync(ct);
    }

    public Task<BackupCar> LiveAsync(Tankstat.Domain.Vehicles.Vehicle vehicle, CancellationToken ct) => ReadAsync(vehicle, trash: false, ct);

    public Task<BackupCar> TrashAsync(Tankstat.Domain.Vehicles.Vehicle vehicle, CancellationToken ct) => ReadAsync(vehicle, trash: true, ct);

    private async Task<BackupCar> ReadAsync(Tankstat.Domain.Vehicles.Vehicle vehicle, bool trash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The trash: the rows' own filter is lifted, and with it their readings' and costs', which went to the trash with them.
        var refuelings = db.Refuelings.AsNoTracking().Include(r => r.Cost).Include(r => r.OdometerReading).Where(r => r.VehicleId == vehicle.Id);
        var expenses = db.Expenses.AsNoTracking().Include(e => e.Cost).Include(e => e.OdometerReading).Where(e => e.VehicleId == vehicle.Id);
        if (trash)
        {
            refuelings = refuelings.IgnoreQueryFilters().Where(r => r.DeletedAt != null);
            expenses = expenses.IgnoreQueryFilters().Where(e => e.DeletedAt != null);
        }
        var schedules = trash ? [] : await db.RecurringExpenses.AsNoTracking().Where(s => s.VehicleId == vehicle.Id).ToListAsync(ct);
        // The schedules each expense marked done (#338: the links a Replace all takes away, and the ones that need not be added again).
        var links = (await db.RecurringCompletions.AsNoTracking()
            .Where(c => db.Expenses.IgnoreQueryFilters().Any(e => e.Id == c.ExpenseId && e.VehicleId == vehicle.Id))
            .Select(c => new { c.ExpenseId, c.RecurringExpenseId }).ToListAsync(ct)).ToLookup(c => c.ExpenseId, c => c.RecurringExpenseId);
        var summary = new BackupCarSummary(vehicle.Id, vehicle.Name, vehicle.LicensePlate, vehicle.FuelType, vehicle.Units, vehicle.OwnerId, null, 0, 0, 0);
        return new BackupCar(summary,
            [.. (await refuelings.ToListAsync(ct)).Select(BackupCarShapes.Of)],
            [.. (await expenses.ToListAsync(ct)).Select(e => BackupCarShapes.Of(e, [.. links[e.Id]]))],
            [.. schedules.Select(BackupCarShapes.Of)]);
    }

    public async Task<ImportHeldBack> HeldBackAsync(Tankstat.Domain.Vehicles.Vehicle vehicle, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Counted as the purges count them (HeldBackLogs): what the review says is held back is what a Replace all would be refused for.
        return new ImportHeldBack(
            await db.CountLogsWithPhotosAsync(Tankstat.Domain.Photos.LogType.Refueling, vehicle.Id, ct),
            await db.CountLogsWithPhotosAsync(Tankstat.Domain.Photos.LogType.Expense, vehicle.Id, ct),
            await db.CountExpensesOnTyreLinesAsync(vehicle.Id, ct));
    }

    public async Task<IReadOnlyList<Guid>> PhotosAsync(Tankstat.Domain.Vehicles.Vehicle vehicle, Tankstat.Domain.Photos.LogType type, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LogPhotos.AsNoTracking().Where(p => p.VehicleId == vehicle.Id && p.LogType == type).Select(p => p.ImageId).ToListAsync(ct);
    }
}
