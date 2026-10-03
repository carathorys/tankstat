using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class ConsumptionPersistenceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 1);

    private static async Task<Vehicle> AddVehicle(TestDatabase db)
    {
        var v = TestData.Vehicle(Owner);
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        return v;
    }

    private static Task Add(TestDatabase db, Vehicle v, int day, long odometer, decimal volume, bool full = true) =>
        db.Get<IRefuelingRepository>().AddAsync(TestData.Refueling(v.OwnerId, Owner, v.Id, Day.AddDays(day), volume, 50, odometer, full), default);

    private static async Task<decimal?[]> Stored(TestDatabase db, Vehicle v) =>
        (await db.Get<IRefuelingRepository>().ListForVehicleAsync(v.Id, new RefuelingQuery(RefuelingSortField.Odometer, SortDirection.Asc), default)).Select(r => r.Consumption).ToArray();

    [Fact]
    public async Task TheConsumptionIsStoredWithTheLog_AndReadBackWithoutCalculating()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await Add(db, car, 0, 1000, 40);
        await Add(db, car, 10, 1500, 30);
        var repo = db.Get<IRefuelingRepository>();

        var logs = await repo.ListAllForVehicleAsync(car.Id, default);
        await repo.SaveConsumptionsAsync(Tankstat.Domain.Vehicles.ConsumptionCalculator.Apply(logs), default);

        Assert.Equal([null, 6m], await Stored(db, car));
    }

    [Fact]
    public async Task SavingConsumptions_ChangesNothingElse()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await Add(db, car, 0, 1000, 40);
        var repo = db.Get<IRefuelingRepository>();
        var log = (await repo.ListAllForVehicleAsync(car.Id, default)).Single();
        var before = (log.Volume, log.TotalCost, log.Odometer, log.Date);
        log.SetConsumption(7.123m);

        await repo.SaveConsumptionsAsync([log], default);

        var loaded = (await repo.ListAllForVehicleAsync(car.Id, default)).Single();
        Assert.Equal((7.123m, before), (loaded.Consumption, (loaded.Volume, loaded.TotalCost, loaded.Odometer, loaded.Date)));
    }

    [Theory]
    [InlineData(RefuelingSortField.Consumption, SortDirection.Desc, new[] { 1500L, 2000L, 1000L })]
    [InlineData(RefuelingSortField.Consumption, SortDirection.Asc, new[] { 1000L, 2000L, 1500L })]
    public async Task Lists_CanBeSortedByConsumption(RefuelingSortField field, SortDirection direction, long[] expectedOdometers)
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await Add(db, car, 0, 1000, 40);
        await Add(db, car, 10, 1500, 45); // 9 per 100 km
        await Add(db, car, 20, 2000, 20); // 4
        var repo = db.Get<IRefuelingRepository>();
        await repo.SaveConsumptionsAsync(Tankstat.Domain.Vehicles.ConsumptionCalculator.Apply(await repo.ListAllForVehicleAsync(car.Id, default)), default);

        var page = await repo.ListForVehicleAsync(car.Id, new RefuelingQuery(field, direction), default);

        Assert.Equal(expectedOdometers, page.Select(r => r.Odometer!.Value));
    }

    [Fact]
    public async Task TheBackfill_CalculatesLogsThatExistedBeforeTheColumn_PerVehicle()
    {
        await using var db = new TestDatabase();
        var first = await AddVehicle(db);
        var second = await AddVehicle(db);
        foreach (var car in new[] { first, second })
        {
            await Add(db, car, 0, 1000, 40);
            await Add(db, car, 10, 1500, 30);
            await Add(db, car, 12, 1600, 10, full: false);
        }
        Assert.All(await Stored(db, first), c => Assert.Null(c)); // nothing calculated yet: the logs were added without the service

        await new DatabaseMigrator(db.Get<IDbContextFactory<AppDbContext>>(), db.Get<ILogger<DatabaseMigrator>>()).BackfillConsumptionAsync(default);

        Assert.Equal([null, 6m, null], await Stored(db, first));
        Assert.Equal([null, 6m, null], await Stored(db, second));
        var said = Assert.Single(db.Log.From<DatabaseMigrator>());
        Assert.Equal(2, said.Values["Vehicles"]);
    }
}
