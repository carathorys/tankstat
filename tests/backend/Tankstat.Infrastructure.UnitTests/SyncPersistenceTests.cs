using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Application.Access;
using Tankstat.Application.Expenses;
using Tankstat.Application.Photos;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Settings;
using Tankstat.Application.Sync;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Settings;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>What devices download for offline use stays current: UpdatedAt on every save and every change a device shows, tombstones for removals.</summary>
public class SyncPersistenceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 1);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    private TestDatabase Database() => new(services: s => s.AddSingleton<TimeProvider>(_clock));

    private static async Task<Vehicle> Car(TestDatabase db)
    {
        var car = TestData.Vehicle(Owner);
        await db.Get<IVehicleRepository>().AddAsync(car, default);
        return car;
    }

    private static async Task<Expense> AddExpense(TestDatabase db, Vehicle car)
    {
        var expense = Expense.Create(Owner, Owner, car.Id, Day, "Service", null, Cost.Create(Owner, car.Id, Day, 100, "EUR"), OdometerReading.Create(Owner, car.Id, Day, 1000));
        await db.Get<IExpenseRepository>().AddAsync(expense, default);
        return expense;
    }

    [Fact]
    public async Task EverySave_SetsUpdatedAt_AndTheConsumptionSavesToo()
    {
        await using var db = Database();
        var car = await Car(db);
        var refuelings = db.Get<IRefuelingRepository>();
        var first = TestData.Refueling(Owner, Owner, car.Id, Day, 40, 50, 1000);
        await refuelings.AddAsync(first, default);
        Assert.Equal(_clock.GetUtcNow(), (await refuelings.FindAsync(first.Id, default))!.UpdatedAt);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var loaded = (await refuelings.FindAsync(first.Id, default))!;
        await refuelings.UpdateAsync(loaded, loaded.Update(Day, 41, 50, "EUR", 1000, true, false, null), default);
        Assert.Equal(_clock.GetUtcNow(), (await refuelings.FindAsync(first.Id, default))!.UpdatedAt);

        var edited = _clock.GetUtcNow();
        var second = TestData.Refueling(Owner, Owner, car.Id, Day.AddDays(10), 30, 50, 1500);
        await refuelings.AddAsync(second, default);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await refuelings.SaveConsumptionsAsync(ConsumptionCalculator.Apply(await refuelings.ListAllForVehicleAsync(car.Id, default)), default);

        // Only the log whose consumption changed is downloaded again; the first one's did not (it starts the chain).
        Assert.Equal(_clock.GetUtcNow(), (await refuelings.FindAsync(second.Id, default))!.UpdatedAt);
        Assert.Equal(edited, (await refuelings.FindAsync(first.Id, default))!.UpdatedAt);
    }

    [Fact]
    public async Task APhotoAddedOrRemoved_MarksItsLogForDownload()
    {
        await using var db = Database();
        var car = await Car(db);
        var expense = await AddExpense(db, car);
        var photos = db.Get<ILogPhotoRepository>();

        _clock.Advance(TimeSpan.FromMinutes(5));
        var photo = LogPhoto.Create(Owner, car.Id, LogType.Expense, expense.Id, Guid.NewGuid(), Owner, _clock.GetUtcNow());
        await photos.AddAsync(photo, default);
        Assert.Equal(_clock.GetUtcNow(), (await db.Get<IExpenseRepository>().FindAsync(expense.Id, default))!.UpdatedAt);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await photos.RemoveAsync(photo, default);
        Assert.Equal(_clock.GetUtcNow(), (await db.Get<IExpenseRepository>().FindAsync(expense.Id, default))!.UpdatedAt);
    }

    [Fact]
    public async Task ASchedulesChanges_MarkTheExpensesThatListIt()
    {
        await using var db = Database();
        var car = await Car(db);
        var expense = await AddExpense(db, car);
        var repo = db.Get<IRecurringExpenseRepository>();
        var oil = RecurringExpense.Create(Owner, Owner, car.Id, "Oil", "Service", null, RecurrenceKind.Time, 12, null, Day, null, 30, 500, _clock.GetUtcNow());
        await repo.AddAsync(oil, default);
        async Task<DateTimeOffset> ExpenseSaved() => (await db.Get<IExpenseRepository>().FindAsync(expense.Id, default))!.UpdatedAt;

        _clock.Advance(TimeSpan.FromMinutes(5));
        await repo.CompleteAsync([oil], [RecurringCompletion.Create(expense.Id, oil.Id)], default);
        Assert.Equal(_clock.GetUtcNow(), await ExpenseSaved()); // it lists the schedule now

        _clock.Advance(TimeSpan.FromMinutes(5));
        await repo.UpdateAsync(oil, default);
        Assert.Equal(_clock.GetUtcNow(), await ExpenseSaved()); // it shows the schedule's title

        _clock.Advance(TimeSpan.FromMinutes(5));
        await repo.RemoveAsync(oil, default);
        Assert.Equal(_clock.GetUtcNow(), await ExpenseSaved()); // it no longer lists it
        var removed = await db.Get<IOfflineFeedRepository>().RemovedSinceAsync(car.Id, _clock.GetUtcNow(), default);
        Assert.Equal([new RemovedEntity(OfflineEntityType.RecurringExpense, oil.Id)], removed);
    }

    [Fact]
    public async Task DeletingAUser_MarksTheExpensesThatListedTheirSchedulesOnOtherPeoplesVehicles()
    {
        await using var db = Database();
        var car = await Car(db);
        var expense = await AddExpense(db, car);
        var repo = db.Get<IRecurringExpenseRepository>();
        var someone = Guid.NewGuid(); // shares the car, made a schedule on it and marked it done with the owner's expense
        var wash = RecurringExpense.Create(Owner, someone, car.Id, "Wash", null, null, RecurrenceKind.Time, 1, null, Day, null, 30, 500, _clock.GetUtcNow());
        await repo.AddAsync(wash, default);
        await repo.CompleteAsync([wash], [RecurringCompletion.Create(expense.Id, wash.Id)], default);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await db.Get<IUserDataRepository>().DeleteUserAsync(someone, null, default);

        Assert.Equal(_clock.GetUtcNow(), (await db.Get<IExpenseRepository>().FindAsync(expense.Id, default))!.UpdatedAt); // it no longer lists it
    }

    [Fact]
    public async Task RemovingForGood_LeavesTombstones_ButNotForWhatExistsAgain()
    {
        await using var db = Database();
        var car = await Car(db);
        var refuelings = db.Get<IRefuelingRepository>();
        var gone = TestData.Refueling(Owner, Owner, car.Id, Day);
        var back = TestData.Refueling(Owner, Owner, car.Id, Day.AddDays(1), odometer: 1100);
        await refuelings.AddAsync(gone, default);
        await refuelings.AddAsync(back, default);
        var since = _clock.GetUtcNow();
        foreach (var log in new[] { gone, back })
        {
            var loaded = (await refuelings.FindAsync(log.Id, default))!;
            loaded.MarkDeleted(_clock.GetUtcNow());
            await refuelings.UpdateAsync(loaded, LinkedChanges.None, default);
        }
        await refuelings.PurgeAsync(OwnerScope.All, default);
        // A device replays the add of one of them: it exists again.
        await refuelings.AddAsync(TestData.Refueling(Owner, Owner, car.Id, Day.AddDays(1), odometer: 1100, id: back.Id), default);

        var removed = await db.Get<IOfflineFeedRepository>().RemovedSinceAsync(car.Id, since, default);

        Assert.Equal([new RemovedEntity(OfflineEntityType.Refueling, gone.Id)], removed);
    }

    [Fact]
    public async Task PagesOfRowsSavedAtTheSameMoment_NeverSkipOrRepeatARow()
    {
        await using var db = Database();
        var car = await Car(db);
        var refuelings = db.Get<IRefuelingRepository>();
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var log = TestData.Refueling(Owner, Owner, car.Id, Day.AddDays(i), odometer: 1000 + i * 100);
            await refuelings.AddAsync(log, default);
            ids.Add(log.Id);
        }
        await using (var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await context.Refuelings.ExecuteUpdateAsync(s => s.SetProperty(r => r.UpdatedAt, _clock.GetUtcNow())); // all at once: the ids decide

        var feed = db.Get<IOfflineFeedRepository>();
        var seen = new List<Guid>();
        OfflineKey? after = null;
        while (true)
        {
            var page = await feed.RefuelingsAsync(new OfflineRows(car.Id, null, null, after, 3), default);
            seen.AddRange(page.Select(r => r.Id));
            if (page.Count < 3) break;
            after = new OfflineKey(page[^1].UpdatedAt, page[^1].Id);
        }

        Assert.Equal(ids.Order(), seen.Order());
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task TheOfflineWindows_RoundTrip_AndGoWithTheirVehicle()
    {
        await using var db = Database();
        var car = await Car(db);
        var repo = db.Get<IOfflineSettingsRepository>();
        await repo.ReplaceAsync(OfflineSettings.Create(Owner, "thisYear", _clock.GetUtcNow()), [OfflineVehicleSetting.Create(Owner, car.Id, "all")], default);
        await repo.ReplaceAsync(OfflineSettings.Create(Owner, "span:P6M", _clock.GetUtcNow()), [OfflineVehicleSetting.Create(Owner, car.Id, "none")], default);

        Assert.Equal("span:P6M", (await repo.FindAsync(Owner, default))!.DefaultWindow);
        Assert.Equal("none", Assert.Single(await repo.ListVehiclesAsync(Owner, default)).Window);

        var loaded = (await db.Get<IVehicleRepository>().FindAsync(car.Id, default))!;
        loaded.MarkDeleted(_clock.GetUtcNow());
        await db.Get<IVehicleRepository>().UpdateAsync(loaded, default);
        await db.Get<IVehicleRepository>().PurgeAsync(OwnerScope.All, default);

        Assert.Empty(await repo.ListVehiclesAsync(Owner, default));
        Assert.Equal([new RemovedEntity(OfflineEntityType.Vehicle, car.Id)], await db.Get<IOfflineFeedRepository>().RemovedSinceAsync(car.Id, _clock.GetUtcNow(), default));
    }
}
