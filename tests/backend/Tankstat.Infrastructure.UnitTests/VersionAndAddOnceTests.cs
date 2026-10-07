using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Versions are stored with the entities, and a second insert with the same id reports itself instead of failing.</summary>
public class VersionAndAddOnceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 1);

    [Fact]
    public async Task TheVersion_IsStored_AndAnUpdateKeepsItsCount()
    {
        await using var db = new TestDatabase();
        var vehicles = db.Get<IVehicleRepository>();
        var refuelings = db.Get<IRefuelingRepository>();
        var car = TestData.Vehicle(Owner);
        await vehicles.AddAsync(car, default);
        var log = TestData.Refueling(Owner, Owner, car.Id, Day);
        await refuelings.AddAsync(log, default);

        var loaded = (await refuelings.FindAsync(log.Id, default))!;
        var changes = loaded.Update(Day, 41, 61, "EUR", 1001, true, false, null);
        await refuelings.UpdateAsync(loaded, changes, default);
        car.Update("Renamed", null, FuelType.Petrol, MeasurementUnits.Metric);
        await vehicles.UpdateAsync(car, default);

        Assert.Equal(2, (await refuelings.FindAsync(log.Id, default))!.Version);
        Assert.Equal(2, (await vehicles.FindAsync(car.Id, default))!.Version);
    }

    [Fact]
    public async Task SavingConsumptions_LeavesTheVersionAlone()
    {
        await using var db = new TestDatabase();
        var car = TestData.Vehicle(Owner);
        await db.Get<IVehicleRepository>().AddAsync(car, default);
        var repo = db.Get<IRefuelingRepository>();
        await repo.AddAsync(TestData.Refueling(Owner, Owner, car.Id, Day, 40, 50, 1000), default);
        await repo.AddAsync(TestData.Refueling(Owner, Owner, car.Id, Day.AddDays(10), 30, 50, 1500), default);

        await repo.SaveConsumptionsAsync(ConsumptionCalculator.Apply(await repo.ListAllForVehicleAsync(car.Id, default)), default);

        Assert.All(await repo.ListAllForVehicleAsync(car.Id, default), r => Assert.Equal(1, r.Version));
    }

    [Fact]
    public async Task TheSameAddTwice_IsReported_NotThrown_AndKeepsTheFirst()
    {
        await using var db = new TestDatabase();
        var car = TestData.Vehicle(Owner);
        var vehicles = db.Get<IVehicleRepository>();
        Assert.True(await vehicles.AddAsync(car, default));
        var id = Guid.NewGuid();
        var expense = Expense.Create(Owner, Owner, car.Id, Day, "Parking", null, Cost.Create(Owner, car.Id, Day, 5, "EUR"), null, id: id);
        var twin = Expense.Create(Owner, Owner, car.Id, Day, "Toll", null, Cost.Create(Owner, car.Id, Day, 9, "EUR"), null, id: id);
        var schedule = RecurringExpense.Create(Owner, Owner, car.Id, "Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, 30, 500, DateTimeOffset.UtcNow, id);

        Assert.True(await db.Get<IExpenseRepository>().AddAsync(expense, default));
        Assert.False(await db.Get<IExpenseRepository>().AddAsync(twin, default));
        Assert.False(await vehicles.AddAsync(Vehicle.Create(Owner, "Twin", null, FuelType.Petrol, MeasurementUnits.Metric, car.Id), default));
        Assert.True(await db.Get<IRecurringExpenseRepository>().AddAsync(schedule, default));

        Assert.Equal("Parking", (await db.Get<IExpenseRepository>().FindAsync(id, default))!.Title);
        Assert.Equal("Car", (await vehicles.FindAsync(car.Id, default))!.Name);
    }

    [Fact]
    public async Task AnInsertThatFailsForAnotherReason_StillFails()
    {
        await using var db = new TestDatabase();
        var log = TestData.Refueling(Owner, Owner, Guid.NewGuid(), Day); // its vehicle does not exist: the foreign key refuses it

        await Assert.ThrowsAnyAsync<Exception>(() => db.Get<IRefuelingRepository>().AddAsync(log, default));
    }
}
