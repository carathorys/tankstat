using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class VehicleRepository(IDbContextFactory<AppDbContext> dbFactory) : IVehicleRepository
{
    public async Task<IReadOnlyList<Vehicle>> ListAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().InScope(scope).OrderBy(v => v.Name).ToListAsync(ct);
    }

    public async Task<Vehicle?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<IReadOnlyList<Vehicle>> ListDeletedAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deleted = await db.Vehicles.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.DeletedAt != null).InScope(scope).ToListAsync(ct);
        return deleted.OrderByDescending(v => v.DeletedAt).ToList();
    }

    public async Task<Vehicle?> FindIncludingDeletedAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task UpdateAsync(Vehicle vehicle, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Vehicles.Update(vehicle);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> PurgeAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var doomed = await db.Vehicles.IgnoreQueryFilters().Where(v => v.DeletedAt != null).InScope(scope).ToListAsync(ct);
        db.Vehicles.RemoveRange(doomed); // refuelings go with them through the database's cascade
        await db.SaveChangesAsync(ct);
        return doomed.Count;
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
    }
}
