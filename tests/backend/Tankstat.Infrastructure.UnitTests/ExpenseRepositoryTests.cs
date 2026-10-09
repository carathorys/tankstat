using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Users;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;
using Tankstat.Domain;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class ExpenseRepositoryTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 1);

    private static async Task<Vehicle> AddVehicle(TestDatabase db)
    {
        var v = TestData.Vehicle(Owner, "Car");
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        return v;
    }

    private static async Task<Expense> Add(TestDatabase db, Vehicle v, string title, DateOnly? date = null, decimal amount = 100, long? odometer = null, string? category = null)
    {
        var d = date ?? Day;
        var cost = Cost.Create(v.OwnerId, v.Id, d, amount, "EUR");
        var reading = odometer is { } o ? OdometerReading.Create(v.OwnerId, v.Id, d, o) : null;
        var expense = Expense.Create(v.OwnerId, Owner, v.Id, d, title, category, cost, reading);
        await db.Get<IExpenseRepository>().AddAsync(expense, default);
        return expense;
    }

    private static async Task<(int Costs, int Readings)> Counts(TestDatabase db)
    {
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return (await ctx.Costs.IgnoreQueryFilters().CountAsync(), await ctx.OdometerReadings.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task AnExpense_IsStoredWithItsCostAndReading_AndLoadedBackTogether()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var saved = await Add(db, car, "Oil change", odometer: 5000, amount: 35000, category: "Service");

        var loaded = (await db.Get<IExpenseRepository>().FindAsync(saved.Id, default))!;

        Assert.Equal(("Oil change", "Service", 35000m, "EUR", 5000L), (loaded.Title, loaded.Category, loaded.Amount, loaded.Currency, loaded.Odometer));
        Assert.Equal((1, 1), await Counts(db));
    }

    [Fact]
    public async Task AnExpenseWithoutOdometer_HasNoReadingRow()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var saved = await Add(db, car, "Wash");

        var loaded = (await db.Get<IExpenseRepository>().FindAsync(saved.Id, default))!;

        Assert.Null(loaded.Odometer);
        Assert.Equal((1, 0), await Counts(db));
    }

    [Fact]
    public async Task Update_InsertsANewReading_AndDeletesADetachedOne()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IExpenseRepository>();
        var saved = await Add(db, car, "Wash");

        var expense = (await repo.FindAsync(saved.Id, default))!;
        await repo.UpdateAsync(expense, expense.Update(Day, "Wash", null, 100, "EUR", 777, null), default);
        Assert.Equal(777L, (await repo.FindAsync(saved.Id, default))!.Odometer);
        Assert.Equal((1, 1), await Counts(db));

        var again = (await repo.FindAsync(saved.Id, default))!;
        await repo.UpdateAsync(again, again.Update(Day, "Wash", null, 100, "EUR", null, null), default);
        Assert.Null((await repo.FindAsync(saved.Id, default))!.Odometer);
        Assert.Equal((1, 0), await Counts(db));
    }

    [Fact]
    public async Task Update_ChangesAnExistingReadingInPlace()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IExpenseRepository>();
        var saved = await Add(db, car, "Oil", odometer: 100);

        var expense = (await repo.FindAsync(saved.Id, default))!;
        await repo.UpdateAsync(expense, expense.Update(Day.AddDays(1), "Oil", null, 5, "HUF", 150, null), default);

        var loaded = (await repo.FindAsync(saved.Id, default))!;
        Assert.Equal((150L, Day.AddDays(1), "HUF"), (loaded.Odometer, loaded.OdometerReading!.Date, loaded.Currency));
        Assert.Equal((1, 1), await Counts(db));
    }

    [Fact]
    public async Task TrashedExpenses_AreHidden_ListedInTheTrash_AndPurgedWithTheirCostAndReading()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IExpenseRepository>();
        var doomed = await Add(db, car, "Old", odometer: 10);
        await Add(db, car, "Kept", odometer: 20);

        var expense = (await repo.FindAsync(doomed.Id, default))!;
        expense.MarkDeleted(DateTimeOffset.UtcNow);
        await repo.UpdateAsync(expense, LinkedChanges.None, default);

        Assert.Equal(["Kept"], (await repo.ListForVehicleAsync(car.Id, new ExpenseQuery(), default)).Select(e => e.Title));
        Assert.Equal(["Old"], (await repo.ListDeletedAsync(OwnerScope.All, new ExpenseQuery(), default)).Select(e => e.Title));
        Assert.Equal(1, await repo.CountDeletedAsync(OwnerScope.All, default));
        var schedule = RecurringExpense.Create(car.OwnerId, car.OwnerId, car.Id, "Oil", null, null, RecurrenceKind.Time, 12, null, Day, null, 30, 500, DateTimeOffset.UtcNow);
        var schedules = db.Get<IRecurringExpenseRepository>();
        await schedules.AddAsync(schedule, default);
        await schedules.CompleteAsync([], [RecurringCompletion.Create(doomed.Id, schedule.Id)], default);

        Assert.Equal(1, (await repo.PurgeAsync(OwnerScope.All, default)).Count);
        Assert.Equal((1, 1), await Counts(db)); // only the kept one's cost and reading remain
        Assert.Empty(await schedules.ListCompletionsForExpensesAsync([doomed.Id], default)); // its links went with it (no foreign key on that side)
        Assert.NotNull(await schedules.FindAsync(schedule.Id, default)); // the schedule stays
        Assert.Null(await repo.FindIncludingDeletedAsync(doomed.Id, default));
    }

    [Fact]
    public async Task PurgingAVehicle_RemovesItsExpenses()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await Add(db, car, "Oil", odometer: 10);
        var vehicles = db.Get<IVehicleRepository>();
        var loaded = (await vehicles.FindAsync(car.Id, default))!;
        loaded.MarkDeleted(DateTimeOffset.UtcNow);
        await vehicles.UpdateAsync(loaded, default);

        await vehicles.PurgeAsync(OwnerScope.All, default);

        Assert.False(await db.Get<IExpenseRepository>().AnyForVehicleAsync(car.Id, default));
        Assert.Equal((0, 0), await Counts(db));
    }

    [Theory]
    [InlineData(ExpenseSortField.Title, SortDirection.Asc, new[] { "alpha", "Beta", "gamma" })]
    [InlineData(ExpenseSortField.Amount, SortDirection.Desc, new[] { "gamma", "alpha", "Beta" })]
    [InlineData(ExpenseSortField.Odometer, SortDirection.Asc, new[] { "gamma", "alpha", "Beta" })] // no odometer sorts first
    [InlineData(ExpenseSortField.Category, SortDirection.Asc, new[] { "alpha", "Beta", "gamma" })]
    [InlineData(ExpenseSortField.Date, SortDirection.Desc, new[] { "Beta", "alpha", "gamma" })]
    public async Task Lists_AreSortedAndPagedInTheDatabase(ExpenseSortField field, SortDirection direction, string[] expected)
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await Add(db, car, "Beta", Day.AddDays(2), 50, 300, "B-cat");
        await Add(db, car, "alpha", Day.AddDays(1), 70, 200, "a-cat");
        await Add(db, car, "gamma", Day, 90, null, "C-cat");

        var page = await db.Get<IExpenseRepository>().ListForVehicleAsync(car.Id, new ExpenseQuery(field, direction), default);

        Assert.Equal(expected, page.Select(e => e.Title));
        var second = await db.Get<IExpenseRepository>().ListForVehicleAsync(car.Id, new ExpenseQuery(field, direction, Skip: 1, Take: 1), default);
        Assert.Equal(expected[1], Assert.Single(second).Title);
    }

    [Fact]
    public async Task TheTrash_IsSortedByWhoLoggedIt_ByVehicle_CaseInsensitively_AndByWhenItWasTrashed()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var zoe = User.CreateLocal("zoe@x.co", "Zoe", false);
        var amy = User.CreateLocal("amy@x.co", "amy", false);
        await users.AddAsync(zoe, default);
        await users.AddAsync(amy, default);
        var van = TestData.Vehicle(Owner, "Van");
        var bus = TestData.Vehicle(Owner, "bus");
        await db.Get<IVehicleRepository>().AddAsync(van, default);
        await db.Get<IVehicleRepository>().AddAsync(bus, default);
        var repo = db.Get<IExpenseRepository>();
        var trashed = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        foreach (var (title, car, by, hoursLater) in new[] { ("one", van, amy, 2), ("two", bus, zoe, 1) })
        {
            var expense = Expense.Create(Owner, by.Id, car.Id, Day, title, null, Cost.Create(Owner, car.Id, Day, 10, "EUR"), null);
            expense.MarkDeleted(trashed.AddHours(hoursLater));
            await repo.AddAsync(expense, default);
        }

        var byCreator = await repo.ListDeletedAsync(OwnerScope.All, new ExpenseQuery(ExpenseSortField.CreatedBy, SortDirection.Asc), default);
        var byVehicle = await repo.ListDeletedAsync(OwnerScope.All, new ExpenseQuery(ExpenseSortField.Vehicle, SortDirection.Asc), default);
        var byVehicleDown = await repo.ListDeletedAsync(OwnerScope.All, new ExpenseQuery(ExpenseSortField.Vehicle, SortDirection.Desc), default);

        Assert.Equal(["one", "two"], byCreator.Select(e => e.Title)); // amy before Zoe
        Assert.Equal(["two", "one"], byVehicle.Select(e => e.Title)); // bus before Van
        Assert.Equal(["one", "two"], byVehicleDown.Select(e => e.Title));
        var byTrashed = await repo.ListDeletedAsync(OwnerScope.All, new ExpenseQuery(ExpenseSortField.DeletedAt, SortDirection.Asc), default);
        Assert.Equal(["two", "one"], byTrashed.Select(e => e.Title)); // trashed first
    }

    [Fact]
    public async Task Categories_AreDistinctAndSorted()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        await Add(db, car, "a", category: "Service");
        await Add(db, car, "b", category: "Parking");
        await Add(db, car, "c", category: "Service");
        await Add(db, car, "d");

        Assert.Equal(["Parking", "Service"], await db.Get<IExpenseRepository>().CategoriesAsync(car.Id, default));
    }

    [Fact]
    public async Task AnUpdate_ThatLetsGoOfTheCostAndTheReading_DeletesThem()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IExpenseRepository>();
        var added = await Add(db, car, "Service", odometer: 150);
        var expense = (await repo.FindAsync(added.Id, default))!;

        // Waiting for a photo that is being read: the amount and the odometer are left to it.
        var changes = expense.Update(Day, "Service", null, null, null, null, null, readingPhotos: true);
        await repo.UpdateAsync(expense, changes, default);

        var loaded = (await repo.FindAsync(added.Id, default))!;
        Assert.Equal(((decimal?)null, (long?)null), (loaded.Amount, loaded.Odometer));
        Assert.Equal((0, 0), await Counts(db)); // the rows it let go of are gone, the log saved first so nothing refers to them
    }

    [Fact]
    public async Task ARefuellingUpdate_ThatLetsGoOfItsCost_DeletesIt()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<Tankstat.Application.Refuelings.IRefuelingRepository>();
        var log = Refueling.Create(car.OwnerId, Owner, car.Id, Day, 40, Cost.Create(car.OwnerId, car.Id, Day, 60, "EUR"), OdometerReading.Create(car.OwnerId, car.Id, Day, 1000), true, false, null);
        await repo.AddAsync(log, default);
        var loaded = (await repo.FindAsync(log.Id, default))!;

        var changes = loaded.Update(Day, 40, null, null, 1000, true, false, null, readingPhotos: true);
        await repo.UpdateAsync(loaded, changes, default);

        Assert.Null((await repo.FindAsync(log.Id, default))!.TotalCost);
        Assert.Equal((0, 1), await Counts(db)); // the cost is gone, the reading stays
    }
}

