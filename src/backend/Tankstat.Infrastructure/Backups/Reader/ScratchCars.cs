using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Backups;
using Tankstat.Application.Imports;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>
/// The cars of an opened file (#336), read from its scratch database as today's types. What was in the trash when the backup was made
/// is left out (the model's query filters hide it), a trashed car's rows with it. Every text handed out is cut to a length
/// (<see cref="BackupCarShapes.Cut"/>): the rows are the uploader's, and the scratch database enforces no lengths.
/// </summary>
internal sealed class ScratchCars(ScratchDatabase database, BackupFileSummary summary) : IBackupCars, IAsyncDisposable
{
    public const int MaxCars = 10_000;

    public BackupFileSummary Summary { get; } = summary;

    public static async Task<ScratchCars> CreateAsync(ScratchDatabase database, BackupManifest manifest, CancellationToken ct)
    {
        await using var db = database.Read();
        var vehicles = await db.Vehicles.OrderBy(v => v.Name).ThenBy(v => v.Id).Take(MaxCars + 1).ToListAsync(ct);
        if (vehicles.Count > MaxCars) throw NotABackup();
        var trashed = await db.Vehicles.IgnoreQueryFilters().CountAsync(v => v.DeletedAt != null, ct);
        var owners = await db.Users.Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var refuelings = await db.Refuelings.GroupBy(r => r.VehicleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var expenses = await db.Expenses.GroupBy(e => e.VehicleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var schedules = await db.RecurringExpenses.GroupBy(s => s.VehicleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var cars = vehicles.Select(v => new BackupCarSummary(
            v.Id, BackupCarShapes.Cut(v.Name) ?? "", BackupCarShapes.Cut(v.LicensePlate), v.FuelType, v.Units, v.OwnerId, BackupCarShapes.Cut(owners.GetValueOrDefault(v.OwnerId)),
            refuelings.GetValueOrDefault(v.Id), expenses.GetValueOrDefault(v.Id), schedules.GetValueOrDefault(v.Id))).ToList();
        return new ScratchCars(database, new BackupFileSummary(
            manifest.CreatedAt, BackupReaderText.Version(manifest.AppVersion), manifest.WrittenBy == BackupManifest.Export, cars, trashed));
    }

    public async Task<BackupCar?> ReadCarAsync(Guid carId, CancellationToken ct)
    {
        if (Summary.Cars.FirstOrDefault(c => c.Id == carId) is not { } car) return null;
        try
        {
            await using var db = database.Read();
            var refuelings = await db.Refuelings.Include(r => r.Cost).Include(r => r.OdometerReading)
                .Where(r => r.VehicleId == carId).OrderBy(r => r.Date).ThenBy(r => r.Id).ToListAsync(ct);
            var expenses = await db.Expenses.Include(e => e.Cost).Include(e => e.OdometerReading)
                .Where(e => e.VehicleId == carId).OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync(ct);
            var schedules = await db.RecurringExpenses.Where(s => s.VehicleId == carId).OrderBy(s => s.Title).ThenBy(s => s.Id).ToListAsync(ct);
            // A visit's link counts only between an expense and a schedule of this car that are both here (neither in the trash).
            var scheduleIds = schedules.Select(s => s.Id).ToHashSet();
            var completed = (await db.RecurringCompletions.Where(c => scheduleIds.Contains(c.RecurringExpenseId)).Select(c => new { c.ExpenseId, c.RecurringExpenseId }).ToListAsync(ct))
                .ToLookup(c => c.ExpenseId, c => c.RecurringExpenseId);
            return new BackupCar(car,
                [.. refuelings.Select(BackupCarShapes.Of)],
                [.. expenses.Select(e => BackupCarShapes.Of(e, [.. completed[e.Id].Order()]))],
                [.. schedules.Select(BackupCarShapes.Of)]);
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidCastException or FormatException or Microsoft.Data.Sqlite.SqliteException)
        {
            throw NotABackup(); // a row that is not its table's: a value no column of ours takes
        }
    }

    public static DomainException NotABackup() => new(BackupImportSessions.NotABackupKey, "This file is not a Tankstat backup or export.");

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
