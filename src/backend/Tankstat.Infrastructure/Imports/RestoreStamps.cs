using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Imports;
using Tankstat.Domain.Sync;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Imports;

/// <summary>
/// Writes the backup's stamps over the rows a partial restore made (#340), around the change tracker: a tracked save would take the version
/// it sets for the one it was loaded with, so these are conditional-free updates, which set <c>UpdatedAt</c> themselves (as every write
/// that bypasses <see cref="SyncInterceptor"/> does). Rows that share a creator and a version are written together, in chunks that stay
/// under SQL Server's 2,100 parameters; rows with a creation time of their own one by one (a car has few).
/// </summary>
internal sealed class RestoreStamps(IDbContextFactory<AppDbContext> dbFactory, TimeProvider clock) : IRestoreStamps
{
    private const int Chunk = 500;

    public async Task StampAsync(RestoreTable table, IReadOnlyCollection<RestoreStamp> rows, Guid by, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        switch (table)
        {
            case RestoreTable.Vehicle: await SyncedAsync(db, db.Vehicles.IgnoreQueryFilters(), rows, by, now, creator: false, ct); break;
            case RestoreTable.Refueling: await SyncedAsync(db, db.Refuelings.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.Expense: await SyncedAsync(db, db.Expenses.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.RecurringExpense: await SyncedAsync(db, db.RecurringExpenses.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.TyreSet: await SyncedAsync(db, db.TyreSets.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.TyreInstallation: await SyncedAsync(db, db.TyreInstallations.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.TyreEntry: await SyncedAsync(db, db.TyreEntries.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.Tyre: await SyncedAsync(db, db.Tyres.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.TyrePlace: await SyncedAsync(db, db.TyrePlaces.IgnoreQueryFilters(), rows, by, now, creator: true, ct); break;
            case RestoreTable.Chart:
                foreach (var row in rows.Where(r => r.CreatedById is not null))
                    await db.VehicleCharts.Where(c => c.Id == row.Id).ExecuteUpdateAsync(s =>
                    {
                        s.SetProperty(c => c.CreatedById, row.CreatedById!.Value);
                        if (row.CreatedAt is { } at) s.SetProperty(c => c.CreatedAt, at);
                    }, ct);
                break;
            case RestoreTable.LogPhoto:
                foreach (var row in rows.Where(r => r.CreatedById is not null))
                    await db.LogPhotos.Where(p => p.Id == row.Id).ExecuteUpdateAsync(s =>
                    {
                        s.SetProperty(p => p.CreatedById, row.CreatedById!.Value);
                        if (row.CreatedAt is { } at) s.SetProperty(p => p.CreatedAt, at);
                    }, ct);
                break;
        }
    }

    public async Task<IReadOnlyDictionary<RestoreTable, IReadOnlySet<Guid>>> IdsAsync(Guid vehicleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        async Task<IReadOnlySet<Guid>> Of(IQueryable<Guid> ids) => (await ids.ToListAsync(ct)).ToHashSet();
        return new Dictionary<RestoreTable, IReadOnlySet<Guid>>
        {
            [RestoreTable.Vehicle] = await Of(db.Vehicles.IgnoreQueryFilters().Where(v => v.Id == vehicleId).Select(v => v.Id)),
            [RestoreTable.Refueling] = await Of(db.Refuelings.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.Expense] = await Of(db.Expenses.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.RecurringExpense] = await Of(db.RecurringExpenses.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.TyreSet] = await Of(db.TyreSets.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.TyreInstallation] = await Of(db.TyreInstallations.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.TyreEntry] = await Of(db.TyreEntries.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.Tyre] = await Of(db.Tyres.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.TyrePlace] = await Of(db.TyrePlaces.IgnoreQueryFilters().Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.Chart] = await Of(db.VehicleCharts.Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
            [RestoreTable.LogPhoto] = await Of(db.LogPhotos.Where(r => r.VehicleId == vehicleId).Select(r => r.Id)),
        };
    }

    public async Task RemoveLeftoverPlacesAsync(Guid vehicleId, IReadOnlyCollection<Guid> placeIds, CancellationToken ct)
    {
        if (placeIds.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var chunk in placeIds.Chunk(Chunk))
            db.TyrePlaces.RemoveRange(await db.TyrePlaces.IgnoreQueryFilters().Where(p => p.VehicleId == vehicleId && p.DeletedAt != null && chunk.Contains(p.Id)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncedAsync<T>(AppDbContext db, IQueryable<T> set, IReadOnlyCollection<RestoreStamp> rows, Guid by, DateTimeOffset now, bool creator, CancellationToken ct)
        where T : class, ISynced
    {
        // A schedule keeps when it was made: one at a time. The rest by what they share.
        var created = db.Model.FindEntityType(typeof(T))?.FindProperty("CreatedAt") is not null;
        var groups = created
            ? rows.Select(r => (Key: (r.CreatedById, r.Version, r.CreatedAt), Ids: (IReadOnlyList<Guid>)[r.Id]))
            : rows.GroupBy(r => (r.CreatedById, r.Version, CreatedAt: (DateTimeOffset?)null)).SelectMany(g => g.Select(r => r.Id).Chunk(Chunk).Select(c => (g.Key, Ids: (IReadOnlyList<Guid>)c)));
        foreach (var (key, ids) in groups)
            await set.Where(e => ids.Contains(EF.Property<Guid>(e, "Id"))).ExecuteUpdateAsync(s =>
            {
                s.SetProperty(e => EF.Property<EntityChange?>(e, nameof(ISynced.LastChange)), EntityChange.RestoredFromBackup);
                s.SetProperty(e => EF.Property<Guid?>(e, nameof(ISynced.ChangedById)), by);
                s.SetProperty(e => EF.Property<DateTimeOffset?>(e, nameof(ISynced.ChangedAt)), now);
                s.SetProperty(e => EF.Property<DateTimeOffset>(e, nameof(ISynced.UpdatedAt)), now);
                if (key.Version is { } version) s.SetProperty(e => EF.Property<int>(e, nameof(ISynced.Version)), version);
                if (creator && key.CreatedById is { } who) s.SetProperty(e => EF.Property<Guid>(e, "CreatedById"), who);
                if (key.CreatedAt is { } at) s.SetProperty(e => EF.Property<DateTimeOffset>(e, "CreatedAt"), at);
            }, ct);
    }
}
