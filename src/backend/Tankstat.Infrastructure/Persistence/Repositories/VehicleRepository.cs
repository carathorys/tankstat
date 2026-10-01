using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class VehicleRepository(IDbContextFactory<AppDbContext> dbFactory) : IVehicleRepository
{
    public async Task<IReadOnlyList<Vehicle>> ListAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Page(db, db.Vehicles.AsNoTracking().InScope(scope, v => v.Id), query).ToListAsync(ct);
    }

    public async Task<int> CountAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.InScope(scope, v => v.Id).CountAsync(ct);
    }

    public async Task<IReadOnlyList<Vehicle>> ListDeletedAsync(OwnerScope scope, VehicleQuery query, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trashed = db.Vehicles.IgnoreQueryFilters().AsNoTracking().Where(v => v.DeletedAt != null).InScope(scope, v => v.Id);
        return await Page(db, trashed, query).ToListAsync(ct);
    }

    public async Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.IgnoreQueryFilters().Where(v => v.DeletedAt != null).InScope(scope, v => v.Id).CountAsync(ct);
    }

    /// <summary>Orders (always ending in the id, so pages are stable) and pages inside the database.</summary>
    private static IQueryable<Vehicle> Page(AppDbContext db, IQueryable<Vehicle> vehicles, VehicleQuery query)
    {
        var q = query.Normalized();
        var desc = q.Direction == SortDirection.Desc;

        var ordered = q.SortBy switch
        {
            VehicleSortField.LicensePlate => Order(vehicles, v => v.LicensePlate == null ? null : v.LicensePlate.ToLower(), desc),
            VehicleSortField.FuelType => Order(vehicles, v => v.FuelType, desc),
            VehicleSortField.Owner => Order(vehicles, v => db.Users.Where(u => u.Id == v.OwnerId).Select(u => u.DisplayName.ToLower()).FirstOrDefault(), desc),
            VehicleSortField.RefuelingCount => Order(vehicles, v => db.Refuelings.Count(r => r.VehicleId == v.Id), desc),
            VehicleSortField.DeletedAt => Order(vehicles, v => v.DeletedAt, desc),
            _ => Order(vehicles, v => v.Name.ToLower(), desc),
        };
        return ordered.ThenBy(v => v.Id).Skip(q.Skip).Take(q.Take);
    }

    private static IOrderedQueryable<Vehicle> Order<TKey>(IQueryable<Vehicle> vehicles, System.Linq.Expressions.Expression<Func<Vehicle, TKey>> key, bool desc) =>
        desc ? vehicles.OrderByDescending(key) : vehicles.OrderBy(key);

    public async Task<Vehicle?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
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

    public async Task<PurgeResult> PurgeAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var doomed = await db.Vehicles.IgnoreQueryFilters().Where(v => v.DeletedAt != null).InScope(scope, v => v.Id).ToListAsync(ct);
        db.Vehicles.RemoveRange(doomed); // refuelings go with them through the database's cascade
        await db.SaveChangesAsync(ct);
        return new PurgeResult(doomed.Count, doomed.Where(v => v.PictureImageId is not null).Select(v => v.PictureImageId!.Value).ToList());
    }

    public async Task<Vehicle?> FindByPictureImageAsync(Guid imageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.PictureImageId == imageId, ct);
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
    }
}
