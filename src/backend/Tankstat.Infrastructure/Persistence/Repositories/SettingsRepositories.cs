using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Settings;
using Tankstat.Domain.Settings;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class UiSettingsRepository(IDbContextFactory<AppDbContext> dbFactory) : IUiSettingsRepository
{
    public async Task<UiSettings?> FindAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
    }

    public async Task SaveAsync(UiSettings settings, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Entry(settings).State = await db.UiSettings.AnyAsync(s => s.UserId == settings.UserId, ct) ? EntityState.Modified : EntityState.Added;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GridSettings>> ListGridsAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GridSettings.AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.GridId).ToListAsync(ct);
    }

    public async Task<GridSettings?> FindGridAsync(Guid userId, string gridId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GridSettings.AsNoTracking().FirstOrDefaultAsync(g => g.UserId == userId && g.GridId == gridId, ct);
    }

    public async Task SaveGridAsync(GridSettings grid, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Entry(grid).State = await db.GridSettings.AnyAsync(g => g.UserId == grid.UserId && g.GridId == grid.GridId, ct) ? EntityState.Modified : EntityState.Added;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveGridAsync(Guid userId, string gridId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GridSettings.Where(g => g.UserId == userId && g.GridId == gridId).ExecuteDeleteAsync(ct) > 0;
    }
}

internal sealed class VehicleOrderRepository(IDbContextFactory<AppDbContext> dbFactory) : IVehicleOrderRepository
{
    public async Task<IReadOnlyList<VehicleOrder>> ListAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.VehicleOrders.AsNoTracking().Where(o => o.UserId == userId).OrderBy(o => o.Position).ThenBy(o => o.VehicleId).ToListAsync(ct);
    }

    public async Task ReplaceAsync(Guid userId, IReadOnlyList<VehicleOrder> order, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.VehicleOrders.Where(o => o.UserId == userId).ExecuteDeleteAsync(ct);
        db.VehicleOrders.AddRange(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
