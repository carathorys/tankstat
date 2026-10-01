using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Refuelings;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class RefuelingRepository(IDbContextFactory<AppDbContext> dbFactory) : IRefuelingRepository
{
    public async Task<IReadOnlyList<Refueling>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Refuelings.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.OdometerKm)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Refueling refueling, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Refuelings.Add(refueling);
        await db.SaveChangesAsync(ct);
    }
}
