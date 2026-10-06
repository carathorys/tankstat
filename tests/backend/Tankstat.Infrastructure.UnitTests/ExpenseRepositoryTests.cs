using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Vehicles;
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
}
