using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class RecurringExpenseServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 1); // the fake clock

    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, default);
        return new Scene(w, alice, bob, car);
    }

    private static RecurringExpenseInput Oil(RecurrenceKind kind = RecurrenceKind.Combined, string title = "Oil change", DateOnly? last = null, long? odometer = 50000) =>
        new(title, "Service", null, kind, kind == RecurrenceKind.Odometer ? null : 12, kind == RecurrenceKind.Time ? null : 15000, last ?? new DateOnly(2026, 1, 15), odometer, null, null);

    [Fact]
    public async Task Add_StoresTheScheduleWithTheDefaultWarnings_AndReportsItsStatus()
    {
        var s = await Setup();

        var added = await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default);

        Assert.Equal((s.Alice.Id, s.Car.Id, 30, 500L), (added.Item.CreatedById, added.Item.VehicleId, added.Item.WarnDays, added.Item.WarnDistance));
        Assert.Equal(s.W.Clock.GetUtcNow(), added.Item.CreatedAt); // who and when
        Assert.Equal(new DateOnly(2027, 1, 15), added.Status.DueDate);
        Assert.Equal(RecurrenceState.Upcoming, added.Status.State);
    }

    [Fact]
    public async Task Add_StartsCountingTodayFromTheCurrentOdometer_WhenNoneIsGiven()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, new DateOnly(2026, 9, 30), 40, 60, 71500, true, default);

        var added = await s.W.RecurringService.AddAsync(s.Car.Id, Oil(odometer: null) with { LastDoneDate = null }, default);

        Assert.Equal((Today, 71500L), (added.Item.LastDoneDate, added.Item.LastDoneOdometer));
        Assert.Equal(86500L, added.Status.DueOdometer);
        Assert.Equal(15000L, added.Status.DistanceLeft); // the calculations start right away
    }

    [Fact]
    public async Task Add_KeepsAnOdometerThatWasGiven_AndDoesNotNeedOneForTimeOnlySchedules()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, new DateOnly(2026, 9, 30), 40, 60, 71500, true, default);

        var given = await s.W.RecurringService.AddAsync(s.Car.Id, Oil(odometer: 70000) with { LastDoneDate = null }, default);
        var timeOnly = await s.W.RecurringService.AddAsync(s.Car.Id, Oil(RecurrenceKind.Time, "Insurance", odometer: null) with { LastDoneDate = null }, default);

        Assert.Equal(70000L, given.Item.LastDoneOdometer);
        Assert.Null(timeOnly.Item.LastDoneOdometer); // not needed, so not made up
    }

    [Fact]
    public async Task Add_NeedsAnOdometer_WhenDistanceCountsAndTheVehicleHasNoReadingYet()
    {
        var s = await Setup();

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.RecurringService.AddAsync(s.Car.Id, Oil(odometer: null) with { LastDoneDate = null }, default));

        Assert.Equal("recurring.odometerRequired", error.Key);
        Assert.Empty(s.W.Recurring.Items);
    }

    [Fact]
    public async Task Update_KeepsTheStartDateWhenNoneIsGiven()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;

        var updated = await s.W.RecurringService.UpdateAsync(item.Id, Oil(title: "Oil and filter") with { LastDoneDate = null }, default);

        Assert.Equal((new DateOnly(2026, 1, 15), "Oil and filter"), (updated.Item.LastDoneDate, updated.Item.Title));
    }

    [Fact]
    public async Task List_IsMostUrgentFirst_AndUsesTheLatestOdometerOfTheVehicle()
    {
        var s = await Setup();
        await s.W.RecurringService.AddAsync(s.Car.Id, Oil(RecurrenceKind.Time, "Insurance", new DateOnly(2025, 12, 1)), default); // due 2026-12-01: upcoming
        await s.W.RecurringService.AddAsync(s.Car.Id, Oil(RecurrenceKind.Odometer, "Tyres", odometer: 50000), default); // due at 65000
        await s.W.RecurringService.AddAsync(s.Car.Id, Oil(RecurrenceKind.Time, "Inspection", new DateOnly(2025, 6, 1)), default); // due 2026-06-01: overdue
        await s.W.RefuelingService.LogAsync(s.Car.Id, new DateOnly(2026, 9, 30), 40, 60, 64800, true, default); // the car is 200 from the tyres

        var list = await s.W.RecurringService.ListAsync(s.Car.Id, default);

        Assert.Equal(["Inspection", "Tyres", "Insurance"], list.Select(i => i.Item.Title));
        Assert.Equal([RecurrenceState.Overdue, RecurrenceState.DueSoon, RecurrenceState.Upcoming], list.Select(i => i.Status.State));
        Assert.Equal(200L, list[1].Status.DistanceLeft);
    }

    [Fact]
    public async Task MarkDone_LogsTheExpense_AndStartsTheNextInterval()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;

        var done = await s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2026, 9, 20), 62000, true, 35000, "HUF"), default);

        var expense = Assert.Single(s.W.Expenses.Items);
        Assert.Equal(("Oil change", "Service", 35000m, "HUF", 62000L), (expense.Title, expense.Category, expense.Amount, expense.Currency, expense.Odometer));
        Assert.Equal((new DateOnly(2026, 9, 20), 62000L), (done.Item.LastDoneDate, done.Item.LastDoneOdometer));
        Assert.Equal(new DateOnly(2027, 9, 20), done.Status.DueDate);
        Assert.Equal(77000L, done.Status.DueOdometer);
    }

    [Fact]
    public async Task MarkDone_WithoutAnExpense_OnlyMovesTheBaseline()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(RecurrenceKind.Time), default)).Item;

        await s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2026, 9, 20), null, false, null, null), default);

        Assert.Empty(s.W.Expenses.Items);
        Assert.Equal(new DateOnly(2026, 9, 20), item.LastDoneDate);
    }

    [Fact]
    public async Task MarkDone_FailsAsAWhole_WhenTheExpenseOrTheScheduleRulesAreBroken()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;
        await s.W.RefuelingService.LogAsync(s.Car.Id, new DateOnly(2026, 9, 1), 40, 60, 60000, true, default);
        var before = (item.LastDoneDate, item.LastDoneOdometer);

        var noAmount = await Assert.ThrowsAsync<DomainException>(() => s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2026, 9, 20), 62000, true, null, null), default));
        var lowerThanARecentReading = await Assert.ThrowsAsync<DomainException>(() => s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2026, 9, 20), 55000, true, 10, "HUF"), default));
        var noOdometer = await Assert.ThrowsAsync<DomainException>(() => s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2026, 9, 20), null, false, null, null), default));
        var future = await Assert.ThrowsAsync<DomainException>(() => s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2026, 11, 1), 62000, false, null, null), default));

        Assert.Equal(["recurring.amountRequired", "odometer.belowPrevious", "recurring.odometerRequired", "refueling.dateInFuture"],
            [noAmount.Key, lowerThanARecentReading.Key, noOdometer.Key, future.Key]);
        Assert.Empty(s.W.Expenses.Items);
        Assert.Equal(before, (item.LastDoneDate, item.LastDoneOdometer));
    }

    [Fact]
    public async Task Update_AndDelete_ChangeTheScheduleItself()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;

        var updated = await s.W.RecurringService.UpdateAsync(item.Id, Oil(RecurrenceKind.Time, "Insurance") with { WarnDays = 14 }, default);
        Assert.Equal(("Insurance", RecurrenceKind.Time, 14), (updated.Item.Title, updated.Item.Kind, updated.Item.WarnDays));

        await s.W.RecurringService.DeleteAsync(item.Id, default);
        Assert.Empty(s.W.Recurring.Items);
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.RecurringService.DeleteAsync(item.Id, default));
    }

    [Fact]
    public async Task FollowsTheAccessRulesOfTheVehiclesLogs()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;

        s.W.Current.SignInAs(s.Bob); // a stranger: nothing is visible, nothing exists
        Assert.Empty(await s.W.RecurringService.ListAsync(s.Car.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.RecurringService.DeleteAsync(item.Id, default));

        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View)); // may look, not touch
        Assert.Single(await s.W.RecurringService.ListAsync(s.Car.Id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(Today, 51000, false, null, null), default));

        s.W.Grants.Items.Clear();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        Assert.Equal(s.Bob.Id, (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(RecurrenceKind.Time, "Insurance"), default)).Item.CreatedById);
    }

    [Fact]
    public async Task ATrashedVehicle_ShowsNoSchedules()
    {
        var s = await Setup();
        await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default);

        await s.W.VehicleService.DeleteAsync(s.Car.Id, default);

        Assert.Empty(await s.W.RecurringService.ListAsync(s.Car.Id, default));
    }
}
