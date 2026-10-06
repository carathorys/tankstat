using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Vehicles;
using Tankstat.Domain;
using Tankstat.TestSupport;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

public class TrashRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly VehicleQuery Newest = new(VehicleSortField.DeletedAt, SortDirection.Desc);
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static async Task<Vehicle> Add(TestDatabase db, Guid owner, string name, DateTimeOffset? deletedAt = null)
    {
        var v = TestData.Vehicle(owner, name, null, FuelType.Petrol);
        if (deletedAt is { } at) v.MarkDeleted(at);
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        return v;
    }

    [Fact]
    public async Task SoftDeletedVehicles_AreHiddenFromNormalQueries_ButStayInTheDatabase()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        var live = await Add(db, Alice, "Live");
        var trashed = await Add(db, Alice, "Trashed", Now);

        Assert.Equal(["Live"], (await repo.ListAsync(OwnerScope.All, new VehicleQuery(), default)).Select(v => v.Name));
        Assert.Null(await repo.FindAsync(trashed.Id, default));
        Assert.NotNull(await repo.FindAsync(live.Id, default));

        var found = (await repo.FindIncludingDeletedAsync(trashed.Id, default))!;
        Assert.Equal(Now, found.DeletedAt); // round-trips as the same instant
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(2, await ctx.Vehicles.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Update_PersistsDeleteAndRestore()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        var v = await Add(db, Alice, "Car");

        v.MarkDeleted(Now);
        await repo.UpdateAsync(v, default);
        Assert.Null(await repo.FindAsync(v.Id, default));

        var trashed = (await repo.FindIncludingDeletedAsync(v.Id, default))!;
        trashed.Restore();
        trashed.Update("Renamed", "ab-1", FuelType.Diesel, MeasurementUnits.Metric);
        await repo.UpdateAsync(trashed, default);

        var again = (await repo.FindAsync(v.Id, default))!;
        Assert.Equal(("Renamed", "AB-1", FuelType.Diesel, (DateTimeOffset?)null), (again.Name, again.LicensePlate, again.FuelType, again.DeletedAt));
    }

    [Fact]
    public async Task ListDeleted_IsNewestFirst_AndScoped()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        await Add(db, Alice, "Older", Now.AddDays(-2));
        await Add(db, Alice, "Newer", Now);
        await Add(db, Bob, "Bobs", Now.AddDays(-1));
        await Add(db, Alice, "Live");

        Assert.Equal(["Newer", "Bobs", "Older"], (await repo.ListDeletedAsync(OwnerScope.All, Newest, default)).Select(v => v.Name));
        Assert.Equal(["Newer", "Older"], (await repo.ListDeletedAsync(OwnerScope.Of([Alice]), Newest, default)).Select(v => v.Name));
        Assert.Empty(await repo.ListDeletedAsync(OwnerScope.Of([]), Newest, default));
    }

    [Fact]
    public async Task Purge_RemovesOnlyTrashedVehiclesInScope_AndTheirRefuelings()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        var refuelings = db.Get<IRefuelingRepository>();
        var doomed = await Add(db, Alice, "Doomed", Now);
        var live = await Add(db, Alice, "Live");
        var others = await Add(db, Bob, "Bobs trashed", Now);
        await refuelings.AddAsync(TestData.Refueling(Alice, Guid.Empty, doomed.Id, new(2026, 9, 1), 10, 10, 1, true), default);
        await refuelings.AddAsync(TestData.Refueling(Alice, Guid.Empty, live.Id, new(2026, 9, 1), 10, 10, 1, true), default);

        var purged = await repo.PurgeAsync(OwnerScope.Of([Alice]), default);

        Assert.Equal(1, purged.Count);
        Assert.Null(await repo.FindIncludingDeletedAsync(doomed.Id, default));
        Assert.NotNull(await repo.FindIncludingDeletedAsync(live.Id, default));
        Assert.NotNull(await repo.FindIncludingDeletedAsync(others.Id, default));
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal([live.Id], await ctx.Refuelings.Select(r => r.VehicleId).ToListAsync()); // cascade removed the other one
    }
}
