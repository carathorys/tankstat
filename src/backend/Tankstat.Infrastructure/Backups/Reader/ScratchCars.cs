using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Backups;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>
/// The cars of an opened file (#336), read from its scratch database as today's types. What was in the trash when the backup was made
/// is left out (the model's query filters hide it), a trashed car's rows with it. Every text handed out is cut to a length
/// (<see cref="BackupCarShapes.Cut"/>): the rows are the uploader's, and the scratch database enforces no lengths.
/// </summary>
internal sealed class ScratchCars(ScratchDatabase database, BackupFileSummary summary, string? archive = null, Tankstat.Application.Backups.BackupPhotos photos = Tankstat.Application.Backups.BackupPhotos.WithPictures) : IBackupCars, IAsyncDisposable
{
    public const int MaxCars = 10_000;

    /// <summary>The most entries of one kind a car is read with: far past what a car gathers (a fill-up a day for a century is 36,500).</summary>
    public const int MaxRowsPerCar = 50_000;

    /// <summary>An ISO 4217 code, as the model maps a cost's currency.</summary>
    private const int MaxCurrencyLength = 3;

    public BackupFileSummary Summary { get; } = summary;

    public static async Task<ScratchCars> CreateAsync(ScratchDatabase database, BackupManifest manifest, CancellationToken ct, string? archive = null)
    {
        await using var db = database.Read();
        // Only what the summary shows is read, and its texts are cut by the database: a row of the file may be a megabyte and the rows
        // together gigabytes (a few megabytes deflated), which whole rows, or every user's name, would put on the heap before any cut.
        const int max = BackupCarShapes.MaxTextLength;
        var vehicles = await db.Vehicles.OrderBy(v => v.Name).ThenBy(v => v.Id).Take(MaxCars + 1)
            .Select(v => new
            {
                v.Id, Name = v.Name.Substring(0, max), LicensePlate = v.LicensePlate!.Substring(0, max), v.FuelType,
                v.Units.Distance, v.Units.Volume, v.OwnerId,
            })
            .ToListAsync(ct);
        if (vehicles.Count > MaxCars) throw NotABackup();
        var trashed = await db.Vehicles.IgnoreQueryFilters().CountAsync(v => v.DeletedAt != null, ct);
        // The foreign keys are off: a row may name any car or owner, so only those of the cars shown are asked for.
        var ids = vehicles.Select(v => v.Id).ToList();
        var ownerIds = vehicles.Select(v => v.OwnerId).Distinct().ToList();
        var owners = await db.Users.Where(u => ownerIds.Contains(u.Id)).Select(u => new { u.Id, DisplayName = u.DisplayName.Substring(0, max) })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var refuelings = await db.Refuelings.Where(r => ids.Contains(r.VehicleId)).GroupBy(r => r.VehicleId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var expenses = await db.Expenses.Where(e => ids.Contains(e.VehicleId)).GroupBy(e => e.VehicleId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var schedules = await db.RecurringExpenses.Where(s => ids.Contains(s.VehicleId)).GroupBy(s => s.VehicleId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var cars = vehicles.Select(v => new BackupCarSummary(
            v.Id, BackupCarShapes.Cut(v.Name) ?? "", BackupCarShapes.Cut(v.LicensePlate), v.FuelType, MeasurementUnits.Create(v.Distance, v.Volume), v.OwnerId,
            BackupCarShapes.Cut(owners.GetValueOrDefault(v.OwnerId)),
            refuelings.GetValueOrDefault(v.Id), expenses.GetValueOrDefault(v.Id), schedules.GetValueOrDefault(v.Id))).ToList();
        return new ScratchCars(database, new BackupFileSummary(
            manifest.CreatedAt, BackupReaderText.Version(manifest.AppVersion), manifest.WrittenBy == BackupManifest.Export, cars, trashed), archive, manifest.Contents.Photos);
    }

    /// <summary>The most photo rows and saved widgets a car is read with: ten photos a log for every log is far past this.</summary>
    public const int MaxMediaRowsPerCar = 100_000;

    public async Task<BackupMedia?> ReadMediaAsync(Guid carId, CancellationToken ct)
    {
        if (Summary.Cars.All(c => c.Id != carId)) return null;
        try
        {
            await using var db = database.Read();
            var picture = await db.Vehicles.Where(v => v.Id == carId).Select(v => v.PictureImageId).FirstOrDefaultAsync(ct);
            var leftOut = photos == Tankstat.Application.Backups.BackupPhotos.LeftOut;
            List<BackupPhoto> logPhotos = [];
            if (!leftOut)
            {
                if (await db.LogPhotos.CountAsync(p => p.VehicleId == carId, ct) > MaxMediaRowsPerCar) throw NotABackup();
                // Only photos of live logs (the query filters hide the trash), in the order a log shows them.
                var fuel = db.Refuelings.Where(r => r.VehicleId == carId).Select(r => r.Id);
                var costs = db.Expenses.Where(e => e.VehicleId == carId).Select(e => e.Id);
                logPhotos = [.. (await db.LogPhotos.AsNoTracking().Where(p => p.VehicleId == carId
                        && ((p.LogType == Tankstat.Domain.Photos.LogType.Refueling && fuel.Contains(p.LogId)) || (p.LogType == Tankstat.Domain.Photos.LogType.Expense && costs.Contains(p.LogId))))
                    .Select(p => new { p.LogType, p.LogId, p.ImageId, p.CreatedAt }).ToListAsync(ct))
                    .OrderBy(p => p.CreatedAt).ThenBy(p => p.ImageId).Select(p => new BackupPhoto(p.LogType, p.LogId, p.ImageId, p.CreatedAt))];
            }
            if (await db.VehicleCharts.CountAsync(c => c.VehicleId == carId, ct) > MaxMediaRowsPerCar
                || await db.VehicleCharts.AnyAsync(c => c.VehicleId == carId && c.Title.Length > Tankstat.Domain.Charts.VehicleChart.MaxTitleLength, ct))
                throw NotABackup();
            var charts = await db.VehicleCharts.AsNoTracking().Where(c => c.VehicleId == carId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(ct);
            var dashboards = new List<BackupDashboard>();
            if (await db.VehicleDashboardLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.VehicleId == carId, ct) is { } shared) dashboards.Add(new BackupDashboard(null, shared.Values));
            foreach (var own in await db.UserDashboardLayouts.AsNoTracking().Where(l => l.VehicleId == carId).Take(MaxMediaRowsPerCar).ToListAsync(ct))
                dashboards.Add(new BackupDashboard(own.UserId, own.Values));
            return new BackupMedia(picture, logPhotos, leftOut, charts, dashboards);
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidCastException or FormatException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        {
            throw NotABackup(); // a row that is not its table's: a value no column of ours takes
        }
    }

    public async Task<byte[]?> ReadImageAsync(Guid carId, BackupPhoto? photo, Guid imageId, int maxBytes, CancellationToken ct)
    {
        if (archive is null || Summary.Cars.All(c => c.Id != carId)) return null;
        // The file's name is made from ids alone, as the writer made it below each root: the log's folder, or the car's picture's.
        var folder = photo is null ? Tankstat.Domain.Images.ImageFolders.VehiclePicture(carId) : Tankstat.Domain.Images.ImageFolders.LogPhotos(carId, photo.LogType, photo.LogId);
        var name = imageId.ToString("N");
        string[] candidates = photo is null
            ? [$"files/pictures/{folder}/{name}"]
            : [$"files/photos/{folder}/{name}", $"files/pictures/{folder}/{name}"];
        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        foreach (var candidate in candidates)
        {
            if (zip.GetEntry(candidate) is not { } entry) continue;
            try
            {
                await using var stream = entry.Open();
                var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, ct)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > maxBytes) break; // one more than allowed is enough to refuse it
                }
                return buffer.ToArray();
            }
            catch (InvalidDataException)
            {
                return null; // a damaged entry (the file is the uploader's) is one the file has not: that photo is missing, the rest goes on
            }
        }
        return null;
    }

    /// <summary>The most rows of one tyre table a car is read with: a lifetime of tyres is a few hundred.</summary>
    public const int MaxTyreRowsPerCar = 10_000;

    public async Task<Tankstat.Application.Tyres.TyreBook?> ReadTyresAsync(Guid carId, CancellationToken ct)
    {
        if (Summary.Cars.All(c => c.Id != carId)) return null;
        try
        {
            await using var db = database.Read();
            // Bounded before a row is read, as a car's logs are: counts first, then texts longer than the app allows.
            if (await db.TyreSets.IgnoreQueryFilters().CountAsync(s => s.VehicleId == carId, ct) > MaxTyreRowsPerCar
                || await db.TyreInstallations.IgnoreQueryFilters().CountAsync(s => s.VehicleId == carId, ct) > MaxTyreRowsPerCar
                || await db.TyreEntries.IgnoreQueryFilters().CountAsync(s => s.VehicleId == carId, ct) > MaxTyreRowsPerCar
                || await db.Tyres.IgnoreQueryFilters().CountAsync(s => s.VehicleId == carId, ct) > MaxTyreRowsPerCar
                || await db.TyrePlaces.IgnoreQueryFilters().CountAsync(s => s.VehicleId == carId, ct) > MaxTyreRowsPerCar
                || await db.TyreSets.IgnoreQueryFilters().AnyAsync(s => s.VehicleId == carId && (s.Name.Length > Tankstat.Domain.Tyres.TyreSet.MaxNameLength
                    || s.Size!.Length > Tankstat.Domain.Tyres.TyreSet.MaxSizeLength), ct)
                || await db.TyreEntries.IgnoreQueryFilters().AnyAsync(e => e.VehicleId == carId && e.Note!.Length > Tankstat.Domain.Tyres.TyreEntry.MaxNoteLength, ct)
                || await db.Tyres.IgnoreQueryFilters().AnyAsync(t => t.VehicleId == carId && (t.Label.Length > Tankstat.Domain.Tyres.Tyre.MaxLabelLength
                    || t.Name!.Length > Tankstat.Domain.Tyres.Tyre.MaxNameLength), ct))
                throw NotABackup();
            return await Persistence.Repositories.TyreRepository.LoadAsync(db, carId, ct);
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidCastException or FormatException or Microsoft.Data.Sqlite.SqliteException)
        {
            throw NotABackup(); // a row that is not its table's: a value no column of ours takes
        }
    }

    public async Task<BackupCar?> ReadCarAsync(Guid carId, CancellationToken ct)
    {
        if (Summary.Cars.FirstOrDefault(c => c.Id == carId) is not { } car) return null;
        // More entries than any car has is no backup of ours: refused before a row is read.
        if (car.Refuelings > MaxRowsPerCar || car.Expenses > MaxRowsPerCar || car.Schedules > MaxRowsPerCar) throw NotABackup();
        try
        {
            await using var db = database.Read();
            // The rows are the uploader's and the scratch database enforces no lengths: a row may be a megabyte and a car's rows together
            // gigabytes. A text longer than the app itself allows was never written by it, so the database is asked first, and such a
            // file is no backup before any of its rows is on the heap.
            if (await db.Refuelings.AnyAsync(r => r.VehicleId == carId && (r.Note!.Length > Refueling.MaxNoteLength || r.Cost!.Currency.Length > MaxCurrencyLength), ct)
                || await db.Expenses.AnyAsync(e => e.VehicleId == carId && (e.Title.Length > Expense.MaxTitleLength || e.Category!.Length > Expense.MaxCategoryLength
                    || e.Note!.Length > Expense.MaxNoteLength || e.Cost!.Currency.Length > MaxCurrencyLength), ct)
                || await db.RecurringExpenses.AnyAsync(s => s.VehicleId == carId && (s.Title.Length > RecurringExpense.MaxTitleLength
                    || s.Category!.Length > RecurringExpense.MaxCategoryLength || s.Note!.Length > RecurringExpense.MaxNoteLength), ct))
                throw NotABackup();
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
