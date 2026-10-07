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

    public Task SaveAsync(UiSettings settings, CancellationToken ct) =>
        UpsertAsync(settings, db => db.UiSettings.AnyAsync(s => s.UserId == settings.UserId, ct), ct);

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

    public Task SaveGridAsync(GridSettings grid, CancellationToken ct) =>
        UpsertAsync(grid, db => db.GridSettings.AnyAsync(g => g.UserId == grid.UserId && g.GridId == grid.GridId, ct), ct);

    /// <summary>
    /// Adds the row or replaces it (the key is never generated). Two first saves at the same time both see no row; the one that loses the
    /// insert becomes an update, so the later value wins instead of an error.
    /// </summary>
    private async Task UpsertAsync<T>(T row, Func<AppDbContext, Task<bool>> exists, CancellationToken ct) where T : class
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isNew = !await exists(db);
        db.Entry(row).State = isNew ? EntityState.Added : EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNew)
        {
            db.ChangeTracker.Clear();
            db.Entry(row).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> RemoveGridAsync(Guid userId, string gridId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GridSettings.Where(g => g.UserId == userId && g.GridId == gridId).ExecuteDeleteAsync(ct) > 0;
    }
}

internal sealed class VehicleOrderRepository(IDbContextFactory<AppDbContext> dbFactory) : IVehicleOrderRepository
{
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

internal sealed class OfflineSettingsRepository(IDbContextFactory<AppDbContext> dbFactory) : IOfflineSettingsRepository
{
    public async Task<OfflineSettings?> FindAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OfflineSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<OfflineVehicleSetting>> ListVehiclesAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.OfflineVehicleSettings.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.VehicleId).ToListAsync(ct);
    }

    public async Task ReplaceAsync(OfflineSettings settings, IReadOnlyList<OfflineVehicleSetting> vehicles, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.OfflineSettings.Where(s => s.UserId == settings.UserId).ExecuteDeleteAsync(ct);
        await db.OfflineVehicleSettings.Where(s => s.UserId == settings.UserId).ExecuteDeleteAsync(ct);
        db.OfflineSettings.Add(settings);
        db.OfflineVehicleSettings.AddRange(vehicles);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
