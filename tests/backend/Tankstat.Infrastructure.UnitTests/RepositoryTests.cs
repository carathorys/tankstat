using Tankstat.Application.Access;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class RepositoryTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task Vehicles_RoundTrip_OrderedByName()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        await repo.AddAsync(TestData.Vehicle(Owner, "Zafira", "z1", FuelType.Lpg), default);
        var octavia = TestData.Vehicle(Owner, "Octavia", null, FuelType.Diesel);
        await repo.AddAsync(octavia, default);

        var all = await repo.ListAsync(OwnerScope.All, new VehicleQuery(), default);
        var found = await repo.FindAsync(octavia.Id, default);

        Assert.Equal(["Octavia", "Zafira"], all.Select(v => v.Name));
        Assert.Equal(FuelType.Diesel, found!.FuelType);
        Assert.Null(await repo.FindAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Vehicles_AreFilteredByOwnerScope()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IVehicleRepository>();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();
        await repo.AddAsync(TestData.Vehicle(alice, "A", null, FuelType.Petrol), default);
        await repo.AddAsync(TestData.Vehicle(bob, "B", null, FuelType.Petrol), default);
        await repo.AddAsync(TestData.Vehicle(carol, "C", null, FuelType.Petrol), default);

        Assert.Equal(["A", "C"], (await repo.ListAsync(OwnerScope.Of([alice, carol]), new VehicleQuery(), default)).Select(v => v.Name));
        Assert.Empty(await repo.ListAsync(OwnerScope.Of([]), new VehicleQuery(), default));
        Assert.Equal(3, (await repo.ListAsync(OwnerScope.All, new VehicleQuery(), default)).Count);
    }

    [Fact]
    public async Task Refuelings_RoundTrip_NewestFirst_AndScopedToVehicle()
    {
        await using var db = new TestDatabase();
        var vehicles = db.Get<IVehicleRepository>();
        var refuelings = db.Get<IRefuelingRepository>();
        var a = TestData.Vehicle(Owner, "A", null, FuelType.Petrol);
        var b = TestData.Vehicle(Owner, "B", null, FuelType.Petrol);
        await vehicles.AddAsync(a, default);
        await vehicles.AddAsync(b, default);
        await refuelings.AddAsync(TestData.Refueling(Owner, Guid.Empty, a.Id, new(2026, 9, 1), 40.123m, 70.55m, 1000, true), default);
        await refuelings.AddAsync(TestData.Refueling(Owner, Guid.Empty, a.Id, new(2026, 10, 1), 30m, 55m, 1500, false), default);
        await refuelings.AddAsync(TestData.Refueling(Owner, Guid.Empty, b.Id, new(2026, 10, 2), 10m, 20m, 50, true), default);

        var list = await refuelings.ListForVehicleAsync(a.Id, new RefuelingQuery(), default);

        Assert.Equal([1500, 1000], list.Select(r => r.Odometer));
        Assert.Equal(40.123m, list[1].Volume);
        Assert.Equal(new DateOnly(2026, 9, 1), list[1].Date);
        Assert.True(list[1].IsFullTank);
    }

    [Fact]
    public async Task Refueling_ForMissingVehicle_ViolatesForeignKey()
    {
        await using var db = new TestDatabase();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Get<IRefuelingRepository>().AddAsync(TestData.Refueling(Owner, Guid.Empty, Guid.NewGuid(), new(2026, 10, 1), 1m, 1m, 1, true), default));
    }
}
