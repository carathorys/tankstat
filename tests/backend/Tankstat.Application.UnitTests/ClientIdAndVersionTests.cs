using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// Adds with an id the client chose are answered with what they created the first time (an answer that never arrived, a device replaying
/// what it did offline), and a change made from an old version of an entity is refused instead of overwriting what happened meanwhile.
/// </summary>
public class ClientIdAndVersionTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    private sealed record Scene(World W, User Alice, User Bob, User Carol, Vehicle Car);

    /// <summary>Alice owns a car whose logs Bob may edit; Carol sees nothing; Alice is signed in.</summary>
    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        var carol = w.AddUser("carol@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Alice car", null, FuelType.Petrol, default);
        await w.Sharing.SetLogAccessAsync(car.Id, bob.Id, AccessLevel.Edit, default);
        return new Scene(w, alice, bob, carol, car);
    }

    private static RefuelingInput Fill(long odometer = 1000, decimal volume = 40) => new(Day, volume, 60, "EUR", odometer, true, null);

    private static async Task<KeyedException> Refused(Func<Task> act) => await Assert.ThrowsAnyAsync<KeyedException>(act);

    // ---- idempotent adds -------------------------------------------------------------------------------------

    [Fact]
    public async Task ALogSentAgainWithItsId_IsTheSameLog_EvenWhenItsRulesWouldNowRefuseIt()
    {
        var s = await Setup();
        var id = Guid.NewGuid();

        var first = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1000), default, id: id);
        await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(500) with { Date = Day.AddDays(-1) }, default); // a reading before it, below it: odd, but allowed
        var again = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1000, volume: 99), default, id: id);

        Assert.Equal(id, first.Id);
        Assert.Same(first, again);
        Assert.Equal(40m, again.Volume); // what was saved the first time, not the values sent again
        Assert.Equal(2, s.W.Refuelings.Items.Count);
    }

    [Fact]
    public async Task SomeoneElsesId_IsRefused()
    {
        var s = await Setup();
        var id = Guid.NewGuid();
        await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, id: id);

        s.W.Current.SignInAs(s.Bob); // may edit the car's logs, but did not create this one
        var refused = await Refused(() => s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, id: id));

        Assert.Equal("sync.idTaken", refused.Key);
        Assert.Single(s.W.Refuelings.Items);
    }

    [Fact]
    public async Task ExpensesSchedulesAndVehicles_AreAddedOnce_ByTheirId()
    {
        var s = await Setup();
        var (expenseId, scheduleId, vehicleId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var expense = new ExpenseInput(Day, "Parking", null, 5, "EUR", null, null);
        var schedule = new RecurringExpenseInput("Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, null, null);

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(expenseId, (await s.W.ExpenseService.AddAsync(s.Car.Id, expense, default, id: expenseId)).Id);
            Assert.Equal(scheduleId, (await s.W.RecurringService.AddAsync(s.Car.Id, schedule, default, scheduleId)).Item.Id);
            Assert.Equal(vehicleId, (await s.W.VehicleService.AddAsync("Van", null, FuelType.Diesel, Domain.Measurements.MeasurementUnits.Metric, default, vehicleId)).Id);
        }

        Assert.Single(s.W.Expenses.Items);
        Assert.Single(s.W.Recurring.Items);
        Assert.Equal(2, s.W.Vehicles.Items.Count); // the car and the van
        s.W.Current.SignInAs(s.Bob);
        Assert.Equal("sync.idTaken", (await Refused(() => s.W.VehicleService.AddAsync("Van", null, FuelType.Diesel, Domain.Measurements.MeasurementUnits.Metric, default, vehicleId))).Key);
    }

    [Fact]
    public async Task ARetryAfterAFirstTryThatFailedAfterSaving_FinishesWhatTheFirstTryLeft()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1000, volume: 40), default);
        var id = Guid.NewGuid();
        s.W.Refuelings.FailNextConsumptions = new InvalidOperationException("database down");
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1500, volume: 30) with { Date = Day.AddDays(10) }, default, id: id));
        var saved = Assert.Single(s.W.Refuelings.Items, r => r.Id == id); // saved...
        saved.SetConsumption(null); // ...but its consumption never reached the database (the fake shares the object the calculator changed)

        var again = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1500, volume: 30) with { Date = Day.AddDays(10) }, default, id: id);

        Assert.Equal(6m, again.Consumption); // 30 l over 500 km
        Assert.Equal(2, s.W.Refuelings.Items.Count);
    }

    [Fact]
    public async Task ARetry_AttachesThePhotosTheFirstTryCouldNotAttach()
    {
        var s = await Setup();
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 }, default);
        var id = Guid.NewGuid();
        s.W.LogPhotos.FailAdds = true; // the first try saves the expense, the photo stays a draft
        await s.W.ExpenseService.AddAsync(s.Car.Id, new ExpenseInput(Day, "Parking", null, 5, "EUR", null, null), default, [draft], id);
        Assert.Empty(s.W.LogPhotos.Items);

        s.W.LogPhotos.FailAdds = false;
        await s.W.ExpenseService.AddAsync(s.Car.Id, new ExpenseInput(Day, "Parking", null, 5, "EUR", null, null), default, [draft], id);

        Assert.Equal(id, Assert.Single(s.W.LogPhotos.Items).LogId);
        Assert.Single(s.W.Expenses.Items);
    }

    [Fact]
    public async Task TheTwinThatLostTheRaceToSave_IsAnsweredWithWhatTheOtherSaved()
    {
        var s = await Setup();
        var (logId, expenseId, scheduleId, vehicleId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        s.W.Refuelings.LoseNextAdd = s.W.Expenses.LoseNextAdd = s.W.Recurring.LoseNextAdd = s.W.Vehicles.LoseNextAdd = true;

        Assert.Equal(logId, (await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, id: logId)).Id);
        Assert.Equal(expenseId, (await s.W.ExpenseService.AddAsync(s.Car.Id, new ExpenseInput(Day, "Parking", null, 5, "EUR", null, null), default, id: expenseId)).Id);
        Assert.Equal(scheduleId, (await s.W.RecurringService.AddAsync(s.Car.Id, new RecurringExpenseInput("Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, null, null), default, scheduleId)).Item.Id);
        Assert.Equal(vehicleId, (await s.W.VehicleService.AddAsync("Van", null, FuelType.Diesel, Domain.Measurements.MeasurementUnits.Metric, default, vehicleId)).Id);

        Assert.Single(s.W.Refuelings.Items);
        Assert.Single(s.W.Expenses.Items);
    }

    [Fact]
    public async Task AnEmptyId_IsRefused()
    {
        var s = await Setup();

        Assert.Equal("id.empty", (await Refused(() => s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, id: Guid.Empty))).Key);
    }

    // ---- versions --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AChangeFromAnOldVersion_IsRefused_AndChangesNothing()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default);
        await s.W.RefuelingService.UpdateAsync(log.Id, Fill(volume: 41), default, expectedVersion: 1); // Alice saw version 1: fine
        Assert.Equal(2, log.Version);

        s.W.Current.SignInAs(s.Bob); // Bob still has version 1 on his device
        var refused = await Refused(() => s.W.RefuelingService.UpdateAsync(log.Id, Fill(volume: 50), default, expectedVersion: 1));

        Assert.Equal("sync.versionMismatch", refused.Key);
        Assert.Equal((1, 2), ((int)refused.Args["expected"]!, (int)refused.Args["actual"]!));
        Assert.Equal((41m, 2), (log.Volume, log.Version));
        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.RefuelingService.DeleteAsync(log.Id, default, expectedVersion: 1))).Key);
        await s.W.RefuelingService.DeleteAsync(log.Id, default, expectedVersion: 2);
        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.RefuelingService.RestoreAsync(log.Id, default, expectedVersion: 2))).Key);
    }

    [Fact]
    public async Task WithoutAVersion_TheLastSaveWins_AsBefore()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default);

        await s.W.RefuelingService.UpdateAsync(log.Id, Fill(volume: 41), default);
        await s.W.RefuelingService.UpdateAsync(log.Id, Fill(volume: 42), default);

        Assert.Equal((42m, 3), (log.Volume, log.Version));
    }

    [Fact]
    public async Task AnEntityTheUserCannotSee_IsNotFound_WhateverTheVersion()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default);

        s.W.Current.SignInAs(s.Carol);

        Assert.Equal("refueling.notFound", (await Refused(() => s.W.RefuelingService.UpdateAsync(log.Id, Fill(), default, expectedVersion: 7))).Key);
    }

    [Fact]
    public async Task ExpensesSchedulesAndVehicles_CheckTheVersionToo()
    {
        var s = await Setup();
        var expense = await s.W.ExpenseService.AddAsync(s.Car.Id, new ExpenseInput(Day, "Parking", null, 5, "EUR", null, null), default);
        var schedule = (await s.W.RecurringService.AddAsync(s.Car.Id, new RecurringExpenseInput("Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, null, null), default)).Item;

        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.ExpenseService.UpdateAsync(expense.Id, new ExpenseInput(Day, "Toll", null, 5, null, null, null), default, expectedVersion: 2))).Key);
        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.ExpenseService.DeleteAsync(expense.Id, default, expectedVersion: 2))).Key);
        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.RecurringService.DeleteAsync(schedule.Id, default, expectedVersion: 2))).Key);
        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.VehicleService.UpdateAsync(s.Car.Id, "Renamed", null, FuelType.Petrol, null, default, expectedVersion: 2))).Key);
        Assert.Equal("sync.versionMismatch", (await Refused(() => s.W.VehicleService.DeleteAsync(s.Car.Id, default, expectedVersion: 2))).Key);
        Assert.Equal(("Parking", "Alice car"), (expense.Title, s.Car.Name));
        Assert.Contains(schedule, s.W.Recurring.Items);
    }

    // ---- marking schedules done again -----------------------------------------------------------------------

    private static RecurringExpenseInput Oil() =>
        new("Oil change", "Service", null, RecurrenceKind.Combined, 12, 15000, new DateOnly(2026, 1, 15), 50000, null, null);

    [Fact]
    public async Task AVisitSentAgainWithItsExpenseId_LogsNothingMore_AndMovesNothing()
    {
        var s = await Setup();
        var oil = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;
        var expenseId = Guid.NewGuid();
        var visit = new MarkDoneInput(new DateOnly(2026, 9, 20), 62000, 35000, "HUF", ExpenseId: expenseId);

        var first = await s.W.RecurringService.MarkDoneAsync([oil.Id], visit, default);
        var again = await s.W.RecurringService.MarkDoneAsync([oil.Id], visit with { Date = new DateOnly(2026, 9, 25), Odometer = 63000 }, default);

        Assert.Equal(expenseId, first.Expense!.Id);
        Assert.Equal(expenseId, again.Expense!.Id);
        Assert.Single(s.W.Expenses.Items);
        Assert.False(s.W.Expenses.Items[0].IsDeleted);
        Assert.Single(s.W.Recurring.Completions);
        Assert.Equal((new DateOnly(2026, 9, 20), 62000L, 2), (oil.LastDoneDate, oil.LastDoneOdometer, oil.Version));
    }

    [Fact]
    public async Task AnExpenseIdOfAnotherVisit_CannotRecordThisOne()
    {
        var s = await Setup();
        var oil = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;
        var tyres = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil() with { Title = "Tyres" }, default)).Item;
        var visit = new MarkDoneInput(new DateOnly(2026, 9, 20), 62000, 35000, "HUF", ExpenseId: Guid.NewGuid());
        await s.W.RecurringService.MarkDoneAsync([oil.Id], visit, default);

        var refused = await Refused(() => s.W.RecurringService.MarkDoneAsync([oil.Id, tyres.Id], visit, default));

        Assert.Equal("sync.idTaken", refused.Key);
        Assert.Equal(new DateOnly(2026, 1, 15), tyres.LastDoneDate); // the other schedule did not move
        Assert.Single(s.W.Expenses.Items);
    }

    [Fact]
    public async Task AVisitWhoseTwinRecordedItFirst_KeepsTheExpense_AndIsAnsweredLikeAVisitSentAgain()
    {
        var s = await Setup();
        var oil = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;
        var visit = new MarkDoneInput(new DateOnly(2026, 9, 20), 62000, 35000, "HUF", ExpenseId: Guid.NewGuid());
        s.W.Recurring.TwinCompletesNext = true; // both tries logged the expense; the twin saved the links first

        var done = await s.W.RecurringService.MarkDoneAsync([oil.Id], visit, default);

        var expense = Assert.Single(s.W.Expenses.Items);
        Assert.False(expense.IsDeleted);
        Assert.Equal(expense.Id, done.Expense!.Id);
        Assert.Equal(oil.Id, Assert.Single(done.Schedules).Item.Id);
        Assert.Equal([(expense.Id, oil.Id)], s.W.Recurring.Completions.Select(c => (c.ExpenseId, c.RecurringExpenseId)));
    }

    [Fact]
    public async Task AVisitTriedAgainAfterItFailed_TakesItsExpenseBackFromTheTrash()
    {
        var s = await Setup();
        var oil = (await s.W.RecurringService.AddAsync(s.Car.Id, Oil(), default)).Item;
        var visit = new MarkDoneInput(new DateOnly(2026, 9, 20), 62000, 35000, "HUF", ExpenseId: Guid.NewGuid());
        s.W.Recurring.FailUpdateWith = new InvalidOperationException("database down");
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.W.RecurringService.MarkDoneAsync([oil.Id], visit, default));
        Assert.True(s.W.Expenses.Items[0].IsDeleted); // undone so a second try does not log the cost twice

        s.W.Recurring.FailUpdateWith = null;
        var done = await s.W.RecurringService.MarkDoneAsync([oil.Id], visit, default);

        var expense = Assert.Single(s.W.Expenses.Items);
        Assert.Same(expense, done.Expense);
        Assert.False(expense.IsDeleted);
        Assert.Equal([(expense.Id, oil.Id)], s.W.Recurring.Completions.Select(c => (c.ExpenseId, c.RecurringExpenseId)));
    }
}
