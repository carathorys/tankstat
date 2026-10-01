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

    public async Task<bool> AnyAsync(Guid vehicleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OdometerReadings.IgnoreQueryFilters().AnyAsync(r => r.VehicleId == vehicleId, ct);
    }
}
