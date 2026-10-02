using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Odometers;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class LogsAndReadingsTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static async Task<Vehicle> AddVehicle(TestDatabase db, Guid? owner = null, MeasurementUnits? units = null)
    {
        var v = TestData.Vehicle(owner ?? Owner, "Car", units: units);
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        return v;
    }

    private static async Task<Refueling> AddLog(TestDatabase db, Vehicle v, DateOnly date, long odometer, decimal volume = 40, decimal cost = 60, string currency = "EUR", Guid? createdBy = null)
    {
        var log = TestData.Refueling(v.OwnerId, createdBy ?? Guid.Empty, v.Id, date, volume, cost, odometer, true, currency);
        await db.Get<IRefuelingRepository>().AddAsync(log, default);
        return log;
    }

    [Fact]
    public async Task ALog_IsStoredWithItsReadingAndCost_AndLoadedBackTogether()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var log = await AddLog(db, car, new(2026, 9, 1), 12_345, volume: 41.5m, cost: 79.9m, currency: "HUF");

        var loaded = (await db.Get<IRefuelingRepository>().FindAsync(log.Id, default))!;

        Assert.Equal((12_345L, 79.9m, "HUF", 41.5m), (loaded.Odometer, loaded.TotalCost, loaded.Currency, loaded.Volume));
        Assert.Equal(log.OdometerReadingId, loaded.OdometerReading.Id);
        Assert.Equal(log.CostId, loaded.Cost.Id);
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await ctx.OdometerReadings.CountAsync());
        Assert.Equal(1, await ctx.Costs.CountAsync());
    }

    [Fact]
    public async Task UpdatingALog_UpdatesItsReadingAndCostRows()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var log = await AddLog(db, car, new(2026, 9, 1), 1000);
        var repo = db.Get<IRefuelingRepository>();

        var loaded = (await repo.FindAsync(log.Id, default))!;
        loaded.Update(new(2026, 9, 5), 30, 45.5m, "USD", 2000, false, "note");
        await repo.UpdateAsync(loaded, default);

        var again = (await repo.FindAsync(log.Id, default))!;
        Assert.Equal((new DateOnly(2026, 9, 5), 30m, 45.5m, "USD", 2000L, false, "note"), (again.Date, again.Volume, again.TotalCost, again.Currency, again.Odometer, again.IsFullTank, again.Note));
        Assert.Equal((new DateOnly(2026, 9, 5), 2000L), (again.OdometerReading.Date, again.OdometerReading.Value));
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await ctx.OdometerReadings.CountAsync()); // updated in place, not duplicated
        Assert.Equal(1, await ctx.Costs.CountAsync());
    }

    [Fact]
    public async Task TrashingALog_HidesItsReadingAndCost_RestoringBringsThemBack()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var log = await AddLog(db, car, new(2026, 9, 1), 1000);
        var repo = db.Get<IRefuelingRepository>();
        var readings = db.Get<IOdometerReadingRepository>();

        var loaded = (await repo.FindAsync(log.Id, default))!;
        loaded.MarkDeleted(DateTimeOffset.UtcNow);
        await repo.UpdateAsync(loaded, default);

        Assert.Null(await repo.FindAsync(log.Id, default));
        Assert.Null(await readings.LatestAsync(car.Id, default)); // a trashed log's reading no longer counts
        Assert.True(await readings.AnyAsync(car.Id, default)); // but it still exists (units stay locked)
        Assert.True(await repo.AnyForVehicleAsync(car.Id, default));
        var trashed = (await repo.FindIncludingDeletedAsync(log.Id, default))!;
        trashed.Restore();
        await repo.UpdateAsync(trashed, default);
        Assert.Equal(1000, (await readings.LatestAsync(car.Id, default))!.Value);
    }

    [Fact]
    public async Task LatestForVehicles_GivesTheLatestReadingOfEachVehicle_InOneQuery()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var other = await AddVehicle(db);
        var empty = await AddVehicle(db);
        await AddLog(db, car, new(2026, 8, 1), 1000);
        await AddLog(db, car, new(2026, 9, 1), 2000);
        await AddLog(db, car, new(2026, 9, 1), 2100); // same day: the higher value wins, like LatestAsync
        await AddLog(db, other, new(2026, 7, 1), 99);
        var readings = db.Get<IOdometerReadingRepository>();

        var latest = await readings.LatestForVehiclesAsync([car.Id, other.Id, empty.Id], default);

        Assert.Equal([2100, 99], [latest[car.Id].Value, latest[other.Id].Value]);
        Assert.False(latest.ContainsKey(empty.Id));
        Assert.Equal((await readings.LatestAsync(car.Id, default))!.Id, latest[car.Id].Id);
        Assert.Empty(await readings.LatestForVehiclesAsync([], default));
    }

    [Fact]
    public async Task NeighbourReadings_AreFoundByDate_IgnoringTheOneBeingEdited()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var other = await AddVehicle(db);
        await AddLog(db, car, new(2026, 8, 1), 1000);
        var middle = await AddLog(db, car, new(2026, 8, 15), 1500);
        await AddLog(db, car, new(2026, 9, 1), 2000);
        await AddLog(db, other, new(2026, 8, 20), 99_999); // another vehicle: never a neighbour
        var readings = db.Get<IOdometerReadingRepository>();

        var previous = await readings.PreviousAsync(car.Id, new(2026, 8, 20), null, default);
        var next = await readings.NextAsync(car.Id, new(2026, 8, 20), null, default);
        var previousWithoutMiddle = await readings.PreviousAsync(car.Id, new(2026, 8, 20), middle.OdometerReadingId, default);

        Assert.Equal(1500, previous!.Value);
        Assert.Equal(2000, next!.Value);
        Assert.Equal(1000, previousWithoutMiddle!.Value);
        Assert.Null(await readings.PreviousAsync(car.Id, new(2026, 8, 1), null, default)); // strictly before
        Assert.Null(await readings.NextAsync(car.Id, new(2026, 9, 1), null, default)); // strictly after
        Assert.Equal(2000, (await readings.LatestAsync(car.Id, default))!.Value);
    }

    [Fact]
    public async Task TheOdometerIsStoredAsABigInteger_WithNoUpperLimit()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);

        var log = await AddLog(db, car, new(2026, 9, 1), 9_000_000_000_000);

        Assert.Equal(9_000_000_000_000, (await db.Get<IRefuelingRepository>().FindAsync(log.Id, default))!.Odometer);
    }

    [Fact]
    public async Task VehicleUnits_ArePersisted()
    {
        await using var db = new TestDatabase();
        var us = await AddVehicle(db, units: MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.UsGallons));
        var metric = await AddVehicle(db);
        var repo = db.Get<IVehicleRepository>();

        Assert.Equal((DistanceUnit.Miles, VolumeUnit.UsGallons), ((await repo.FindAsync(us.Id, default))!.Units.Distance, (await repo.FindAsync(us.Id, default))!.Units.Volume));
        Assert.Equal(MeasurementUnits.Metric, (await repo.FindAsync(metric.Id, default))!.Units);

        var loaded = (await repo.FindAsync(metric.Id, default))!;
        loaded.Update("Car", null, FuelType.Petrol, MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.ImperialGallons));
        await repo.UpdateAsync(loaded, default);
        Assert.Equal(VolumeUnit.ImperialGallons, (await repo.FindAsync(metric.Id, default))!.Units.Volume);
    }

    [Fact]
    public async Task DifferentCurrenciesPerLog_AreKept()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await AddLog(db, car, new(2026, 9, 1), 100, currency: "HUF", cost: 18_500);
        await AddLog(db, car, new(2026, 9, 2), 200, currency: "EUR", cost: 52.25m);

        var list = await db.Get<IRefuelingRepository>().ListForVehicleAsync(car.Id, new RefuelingQuery(), default);

        Assert.Equal([("EUR", 52.25m), ("HUF", 18_500m)], list.Select(r => (r.Currency, r.TotalCost)));
    }

    // ---- sorting and paging ---------------------------------------------------------------------------------

    private static async Task<Guid> SeedForSorting(TestDatabase db)
    {
        var users = db.Get<IUserRepository>();
        var zoe = User.CreateLocal("zoe@x.co", "Zoe", false);
        var amy = User.CreateLocal("amy@x.co", "amy", false);
        await users.AddAsync(zoe, default);
        await users.AddAsync(amy, default);
        var car = await AddVehicle(db);
        await AddLog(db, car, new(2026, 9, 1), 3000, volume: 50, cost: 100, createdBy: zoe.Id);   // 2.0 per unit
        await AddLog(db, car, new(2026, 9, 2), 1000, volume: 20, cost: 70, createdBy: amy.Id);    // 3.5
        await AddLog(db, car, new(2026, 9, 3), 2000, volume: 40, cost: 40, createdBy: zoe.Id);    // 1.0
        return car.Id;
    }

    [Theory]
    [InlineData(RefuelingSortField.Date, new[] { "2026-09-01", "2026-09-02", "2026-09-03" })]
    [InlineData(RefuelingSortField.Volume, new[] { "2026-09-02", "2026-09-03", "2026-09-01" })]
    [InlineData(RefuelingSortField.TotalCost, new[] { "2026-09-03", "2026-09-02", "2026-09-01" })]
    [InlineData(RefuelingSortField.Odometer, new[] { "2026-09-02", "2026-09-03", "2026-09-01" })]
    [InlineData(RefuelingSortField.PricePerUnit, new[] { "2026-09-03", "2026-09-01", "2026-09-02" })]
    public async Task SortsAscending_ByEveryNumericField_AndDescendingReverses(RefuelingSortField field, string[] expectedDates)
    {
        await using var db = new TestDatabase();
        var car = await SeedForSorting(db);
        var repo = db.Get<IRefuelingRepository>();

        var asc = await repo.ListForVehicleAsync(car, new RefuelingQuery(field, SortDirection.Asc), default);
        var desc = await repo.ListForVehicleAsync(car, new RefuelingQuery(field, SortDirection.Desc), default);

        Assert.Equal(expectedDates, asc.Select(r => r.Date.ToString("yyyy-MM-dd")));
        Assert.Equal(expectedDates.Reverse(), desc.Select(r => r.Date.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task SortsByWhoLoggedIt_CaseInsensitively()
    {
        await using var db = new TestDatabase();
        var car = await SeedForSorting(db);

        var list = await db.Get<IRefuelingRepository>().ListForVehicleAsync(car, new RefuelingQuery(RefuelingSortField.CreatedBy, SortDirection.Asc), default);

        Assert.Equal(["amy", "Zoe", "Zoe"], (await Task.WhenAll(list.Select(async r => (await db.Get<IUserRepository>().FindByIdAsync(r.CreatedById, default))!.DisplayName))));
    }

    [Fact]
    public async Task PagesAreStable_AndTheCountIgnoresPaging()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        for (var i = 0; i < 12; i++) await AddLog(db, car, new(2026, 9, 1), 1000); // identical sort keys
        var repo = db.Get<IRefuelingRepository>();

        var seen = new List<Guid>();
        for (var skip = 0; skip < 12; skip += 5)
            seen.AddRange((await repo.ListForVehicleAsync(car.Id, new RefuelingQuery(RefuelingSortField.Date, SortDirection.Desc, skip, 5), default)).Select(r => r.Id));

        Assert.Equal(12, seen.Distinct().Count());
        Assert.Equal(12, await repo.CountForVehicleAsync(car.Id, default));
    }

    // ---- trash, scope and permanent deletion ------------------------------------------------------------------

    [Fact]
    public async Task Trash_IsScopedByOwnerOrByGrantedVehicle()
    {
        await using var db = new TestDatabase();
        var mine = await AddVehicle(db, owner: Owner);
        var theirs = await AddVehicle(db, owner: Guid.NewGuid());
        var granted = await AddVehicle(db, owner: Guid.NewGuid());
        var repo = db.Get<IRefuelingRepository>();
        foreach (var v in new[] { mine, theirs, granted })
        {
            var log = (await repo.FindAsync((await AddLog(db, v, new(2026, 9, 1), 1000)).Id, default))!;
            log.MarkDeleted(DateTimeOffset.UtcNow);
            await repo.UpdateAsync(log, default);
        }

        Assert.Equal(3, await repo.CountDeletedAsync(OwnerScope.All, default));
        Assert.Equal(1, await repo.CountDeletedAsync(OwnerScope.Of([Owner]), default));
        Assert.Equal(2, await repo.CountDeletedAsync(OwnerScope.Of([Owner], [granted.Id]), default)); // owner OR granted vehicle
        Assert.Equal(1, await repo.CountDeletedAsync(OwnerScope.Of([], [granted.Id]), default));
        Assert.Equal(0, await repo.CountDeletedAsync(OwnerScope.Of([]), default));
        Assert.Equal(2, (await repo.ListDeletedAsync(OwnerScope.Of([Owner], [granted.Id]), new RefuelingQuery(RefuelingSortField.Vehicle, SortDirection.Asc), default)).Count);
    }

    [Fact]
    public async Task Purge_RemovesTheTrashedLogsInScope_WithTheirReadingsAndCosts()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IRefuelingRepository>();
        var doomed = (await repo.FindAsync((await AddLog(db, car, new(2026, 9, 1), 1000)).Id, default))!;
        var kept = await AddLog(db, car, new(2026, 9, 2), 2000);
        doomed.MarkDeleted(DateTimeOffset.UtcNow);
        await repo.UpdateAsync(doomed, default);

        var purged = await repo.PurgeAsync(OwnerScope.Of([Owner]), default);

        Assert.Equal(1, purged.Count);
        Assert.Equal(kept.Id, Assert.Single(await repo.ListForVehicleAsync(car.Id, new RefuelingQuery(), default)).Id);
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal([kept.OdometerReadingId], await ctx.OdometerReadings.IgnoreQueryFilters().Select(r => r.Id).ToListAsync());
        Assert.Equal([kept.CostId], await ctx.Costs.IgnoreQueryFilters().Select(c => c.Id).ToListAsync());
    }

    [Fact]
    public async Task Purge_OutsideTheScope_DoesNothing()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IRefuelingRepository>();
        var log = (await repo.FindAsync((await AddLog(db, car, new(2026, 9, 1), 1000)).Id, default))!;
        log.MarkDeleted(DateTimeOffset.UtcNow);
        await repo.UpdateAsync(log, default);

        Assert.Equal(0, (await repo.PurgeAsync(OwnerScope.Of([Guid.NewGuid()]), default)).Count);
        Assert.NotNull(await repo.FindIncludingDeletedAsync(log.Id, default));
    }

    [Fact]
    public async Task DeletingAVehicleForGood_TakesItsLogsReadingsAndCostsWithIt()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await AddLog(db, car, new(2026, 9, 1), 1000);
        var vehicles = db.Get<IVehicleRepository>();
        var loaded = (await vehicles.FindAsync(car.Id, default))!;
        loaded.MarkDeleted(DateTimeOffset.UtcNow);
        await vehicles.UpdateAsync(loaded, default);

        await vehicles.PurgeAsync(OwnerScope.All, default);

        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, await ctx.Refuelings.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await ctx.OdometerReadings.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await ctx.Costs.IgnoreQueryFilters().CountAsync());
    }

    // ---- vehicle scope with granted vehicles ----------------------------------------------------------------

    [Fact]
    public async Task VehicleLists_IncludeGrantedVehicles_NotJustOwnedOnes()
    {
        await using var db = new TestDatabase();
        var mine = await AddVehicle(db, owner: Owner);
        var granted = await AddVehicle(db, owner: Guid.NewGuid());
        await AddVehicle(db, owner: Guid.NewGuid()); // someone else's, no grant
        var repo = db.Get<IVehicleRepository>();
        var scope = OwnerScope.Of([Owner], [granted.Id]);

        var list = await repo.ListAsync(scope, new VehicleQuery(), default);

        Assert.Equivalent(new[] { mine.Id, granted.Id }, list.Select(v => v.Id));
        Assert.Equal(2, await repo.CountAsync(scope, null, default));
    }

    // ---- resource grants --------------------------------------------------------------------------------------

    [Fact]
    public async Task ResourceGrants_RoundTrip_AreUniquePerUserVehicleAndFeature()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var bob = User.CreateLocal("bob@x.co", null, false);
        await users.AddAsync(bob, default);
        var car = await AddVehicle(db);
        var repo = db.Get<IResourceGrantRepository>();
        var grant = ResourceGrant.Create(ResourceType.Vehicle, car.Id, bob.Id, GrantedFeature.Logs, AccessLevel.Edit);
        await repo.AddAsync(grant, default);

        var loaded = (await repo.FindAsync(ResourceType.Vehicle, car.Id, bob.Id, GrantedFeature.Logs, default))!;
        loaded.ChangeLevel(AccessLevel.Delete);
        await repo.UpdateAsync(loaded, default);

        Assert.Equal(AccessLevel.Delete, Assert.Single(await repo.ListForGranteeAsync(bob.Id, ResourceType.Vehicle, GrantedFeature.Logs, default)).Level);
        Assert.Equal(bob.Id, Assert.Single(await repo.ListForResourceAsync(ResourceType.Vehicle, car.Id, default)).GranteeId);
        Assert.Empty(await repo.ListForGranteeAsync(Guid.NewGuid(), ResourceType.Vehicle, GrantedFeature.Logs, default));
        await Assert.ThrowsAnyAsync<Exception>(() => repo.AddAsync(ResourceGrant.Create(ResourceType.Vehicle, car.Id, bob.Id, GrantedFeature.Logs, AccessLevel.Edit), default));

        await repo.RemoveAsync(loaded, default);
        Assert.Empty(await repo.ListForResourceAsync(ResourceType.Vehicle, car.Id, default));
    }

    [Fact]
    public async Task ResourceGrants_GoWhenTheUserIsDeleted_ButNotTheVehicle()
    {
        await using var db = new TestDatabase();
        var bob = User.CreateLocal("bob@x.co", null, false);
        await db.Get<IUserRepository>().AddAsync(bob, default);
        var car = await AddVehicle(db);
        var repo = db.Get<IResourceGrantRepository>();
        await repo.AddAsync(ResourceGrant.Create(ResourceType.Vehicle, car.Id, bob.Id, GrantedFeature.Logs, AccessLevel.Edit), default);

        await using (var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            ctx.Users.Remove(await ctx.Users.SingleAsync(u => u.Id == bob.Id));
            await ctx.SaveChangesAsync();
        }

        Assert.Empty(await repo.ListForResourceAsync(ResourceType.Vehicle, car.Id, default));
        Assert.NotNull(await db.Get<IVehicleRepository>().FindAsync(car.Id, default));
    }
}
