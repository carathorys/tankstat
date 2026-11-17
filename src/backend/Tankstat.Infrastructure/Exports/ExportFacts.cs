using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Exports;
using Tankstat.Domain.Photos;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Exports;

/// <summary>
/// The person's own cars for the Export tab (#341), counted by the database: what each holds (what is in the trash left out) and what its
/// photos and picture add (<c>Images.SizeBytes</c>, no file read), with a rough size of the rows themselves.
/// </summary>
internal sealed class ExportFacts(IDbContextFactory<AppDbContext> dbFactory) : IExportFacts
{
    /// <summary>A row of the file is about this many bytes compressed, for the estimate only.</summary>
    private const long BytesPerRow = 120;

    public async Task<IReadOnlyList<ExportCarOffer>> OwnCarsAsync(Guid? ownerId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cars = await db.Vehicles.AsNoTracking().Where(v => ownerId == null || v.OwnerId == ownerId)
            .OrderBy(v => v.Name).ThenBy(v => v.Id).Select(v => new { v.Id, v.Name, v.LicensePlate, v.PictureImageId }).ToListAsync(ct);
        var ids = cars.Select(c => c.Id).ToList();
        var fuel = await db.Refuelings.Where(r => ids.Contains(r.VehicleId)).GroupBy(r => r.VehicleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var costs = await db.Expenses.Where(e => ids.Contains(e.VehicleId)).GroupBy(e => e.VehicleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var plans = await db.RecurringExpenses.Where(s => ids.Contains(s.VehicleId)).GroupBy(s => s.VehicleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var sets = await db.TyreSets.Where(s => ids.Contains(s.VehicleId)).GroupBy(s => s.VehicleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var charts = await db.VehicleCharts.Where(c => ids.Contains(c.VehicleId)).GroupBy(c => c.VehicleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        // The photos of live logs only, and their sizes.
        var fuelIds = db.Refuelings.Where(r => ids.Contains(r.VehicleId)).Select(r => r.Id);
        var costIds = db.Expenses.Where(e => ids.Contains(e.VehicleId)).Select(e => e.Id);
        var photos = await db.LogPhotos.Where(p => ids.Contains(p.VehicleId)
                && ((p.LogType == LogType.Refueling && fuelIds.Contains(p.LogId)) || (p.LogType == LogType.Expense && costIds.Contains(p.LogId))))
            .Join(db.Images, p => p.ImageId, i => i.Id, (p, i) => new { p.VehicleId, i.SizeBytes })
            .GroupBy(x => x.VehicleId).Select(g => new { g.Key, N = g.Count(), Bytes = g.Sum(x => x.SizeBytes) }).ToListAsync(ct);
        var pictureIds = cars.Where(c => c.PictureImageId != null).Select(c => c.PictureImageId!.Value).ToList();
        var pictures = await db.Images.Where(i => pictureIds.Contains(i.Id)).Select(i => new { i.Id, i.SizeBytes }).ToDictionaryAsync(i => i.Id, i => i.SizeBytes, ct);
        return [.. cars.Select(c =>
        {
            var photo = photos.FirstOrDefault(p => p.Key == c.Id);
            var (f, e, s) = (fuel.GetValueOrDefault(c.Id), costs.GetValueOrDefault(c.Id), plans.GetValueOrDefault(c.Id));
            return new ExportCarOffer(c.Id, c.Name, c.LicensePlate, f, e, s, sets.GetValueOrDefault(c.Id), charts.GetValueOrDefault(c.Id), photo?.N ?? 0, photo?.Bytes ?? 0,
                c.PictureImageId is { } picture ? pictures.GetValueOrDefault(picture) : 0, (f + e + s) * 2 * BytesPerRow);
        })];
    }

}
