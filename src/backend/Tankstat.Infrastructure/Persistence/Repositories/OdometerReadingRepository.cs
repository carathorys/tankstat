using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Odometers;
using Tankstat.Domain.Odometers;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class OdometerReadingRepository(IDbContextFactory<AppDbContext> dbFactory) : IOdometerReadingRepository
{
    public async Task<OdometerReading?> PreviousAsync(Guid vehicleId, DateOnly date, Guid? exceptReadingId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OdometerReadings.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId && r.Date < date && r.Id != exceptReadingId)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefaultAsync(ct);
    }

    public async Task<OdometerReading?> NextAsync(Guid vehicleId, DateOnly date, Guid? exceptReadingId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OdometerReadings.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId && r.Date > date && r.Id != exceptReadingId)
            .OrderBy(r => r.Date).ThenBy(r => r.Value).FirstOrDefaultAsync(ct);
    }

    public async Task<OdometerReading?> LatestAsync(Guid vehicleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OdometerReadings.AsNoTracking().Where(r => r.VehicleId == vehicleId)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Value).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, OdometerReading>> LatestForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return new Dictionary<Guid, OdometerReading>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The readings of the latest day of each vehicle (one query on every provider); the highest value of that day wins, as in LatestAsync.
        var latestDay = await db.OdometerReadings.AsNoTracking()
            .Where(r => vehicleIds.Contains(r.VehicleId) && r.Date == db.OdometerReadings.Where(o => o.VehicleId == r.VehicleId).Max(o => o.Date))
            .ToListAsync(ct);
        return latestDay.GroupBy(r => r.VehicleId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Value).First());
    }

    public async Task<bool> AnyAsync(Guid vehicleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OdometerReadings.IgnoreQueryFilters().AnyAsync(r => r.VehicleId == vehicleId, ct);
    }
}
