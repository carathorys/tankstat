using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Recurring;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class RecurringExpenseRepositoryTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Vehicle> AddVehicle(TestDatabase db, Guid? owner = null)
    {
        var v = TestData.Vehicle(owner ?? Owner, "Car " + Guid.NewGuid().ToString("N")[..4]);
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        return v;
    }

    private static RecurringExpense Item(Vehicle v, string title, Guid? createdBy = null, RecurrenceKind kind = RecurrenceKind.Combined) =>
        RecurringExpense.Create(v.OwnerId, createdBy ?? v.OwnerId, v.Id, title, "Service", "note", kind, 12, 15000, Day, 50000, 30, 500, Now);

    private static async Task<AppDbContext> Context(TestDatabase db) => await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    [Fact]
    public async Task ListForVehicles_LoadsTheItemsOfTheGivenVehiclesOnly_ByTitle()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var van = await AddVehicle(db);
        var other = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        foreach (var item in new[] { Item(car, "Tyres"), Item(van, "Insurance"), Item(car, "Oil"), Item(other, "Not asked for") }) await repo.AddAsync(item, default);

        var loaded = await repo.ListForVehiclesAsync([car.Id, van.Id], default);

        Assert.Equal(["Insurance", "Oil", "Tyres"], loaded.Select(i => i.Title));
        Assert.Empty(await repo.ListForVehiclesAsync([], default));
    }

    [Fact]
    public async Task ListVehicleIds_FindsTheVehiclesInTheScopeWithSchedules_LeavingOutTrashedOnes()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var van = await AddVehicle(db);
        var granted = await AddVehicle(db, Guid.NewGuid());
        var foreign = await AddVehicle(db, Guid.NewGuid());
        var empty = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        foreach (var item in new[] { Item(car, "Oil"), Item(car, "Tyres"), Item(van, "Tax"), Item(granted, "Oil"), Item(foreign, "Oil") }) await repo.AddAsync(item, default);
        van.MarkDeleted(Now);
        await db.Get<IVehicleRepository>().UpdateAsync(van, default);

        var ids = await repo.ListVehicleIdsAsync(OwnerScope.Of([Owner], [granted.Id]), default);

        Assert.Equal(new[] { car.Id, granted.Id }.Order(), ids.Order());
        Assert.DoesNotContain(empty.Id, await repo.ListVehicleIdsAsync(OwnerScope.All, default));
    }

    [Fact]
    public async Task AnItem_IsStoredAndLoadedBack_WithEveryField()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        var item = Item(car, "Oil change");
        await repo.AddAsync(item, default);

        var loaded = (await repo.FindAsync(item.Id, default))!;

        Assert.Equal(("Oil change", "Service", "note", RecurrenceKind.Combined), (loaded.Title, loaded.Category, loaded.Note, loaded.Kind));
        Assert.Equal((12, 15000L, Day, 50000L, 30, 500L), (loaded.IntervalMonths, loaded.IntervalDistance, loaded.LastDoneDate, loaded.LastDoneOdometer, loaded.WarnDays, loaded.WarnDistance));
        Assert.Equal((car.OwnerId, car.Id), (loaded.OwnerId, loaded.VehicleId));
        Assert.Equal(Now, loaded.CreatedAt); // stored as UTC and read back as the same moment
    }

    [Fact]
    public async Task List_IsPerVehicle_ByTitle()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var other = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        foreach (var title in new[] { "Tyres", "Insurance", "Oil" }) await repo.AddAsync(Item(car, title), default);
        await repo.AddAsync(Item(other, "Elsewhere"), default);

        var list = await repo.ListForVehicleAsync(car.Id, default);

        Assert.Equal(["Insurance", "Oil", "Tyres"], list.Select(i => i.Title));
    }

    [Fact]
    public async Task UpdateAndRemove_PersistTheChange()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        var item = Item(car, "Oil");
        await repo.AddAsync(item, default);

        var loaded = (await repo.FindAsync(item.Id, default))!;
        loaded.MarkDone(new DateOnly(2026, 12, 1), 61000);
        await repo.UpdateAsync(loaded, default);
        var again = (await repo.FindAsync(item.Id, default))!;
        Assert.Equal((new DateOnly(2026, 12, 1), 61000L), (again.LastDoneDate, again.LastDoneOdometer));

        await repo.RemoveAsync(again, default);
        Assert.Null(await repo.FindAsync(item.Id, default));
    }

    [Fact]
    public async Task PurgingAVehicle_TakesItsItemsWithIt()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var keep = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        await repo.AddAsync(Item(car, "Gone"), default);
        await repo.AddAsync(Item(keep, "Stays"), default);
        var vehicles = db.Get<IVehicleRepository>();
        var loaded = (await vehicles.FindAsync(car.Id, default))!;
        loaded.MarkDeleted(Now);
        await vehicles.UpdateAsync(loaded, default);

        await vehicles.PurgeAsync(Tankstat.Application.Access.OwnerScope.All, default);

        await using var ctx = await Context(db);
        Assert.Equal(["Stays"], ctx.RecurringExpenses.Select(i => i.Title).ToList());
    }

    [Fact]
    public async Task DeletingAUser_MovesTheirItemsToTheTarget_OrPurgesThem()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var alice = User.CreateLocal("alice@x.co", null, false);
        var bob = User.CreateLocal("bob@x.co", null, false);
        var carol = User.CreateLocal("carol@x.co", null, false);
        foreach (var u in new[] { alice, bob, carol }) await users.AddAsync(u, default);
        var aliceCar = await AddVehicle(db, alice.Id);
        var bobCar = await AddVehicle(db, bob.Id);
        var repo = db.Get<IRecurringExpenseRepository>();
        var own = Item(aliceCar, "Alice's");
        var onBobs = Item(bobCar, "By Alice on Bob's", createdBy: alice.Id);
        await repo.AddAsync(own, default);
        await repo.AddAsync(onBobs, default);

        await db.Get<IUserDataRepository>().DeleteUserAsync(alice.Id, carol.Id, default); // move to Carol

        await using (var ctx = await Context(db))
        {
            var items = ctx.RecurringExpenses.AsNoTracking().ToDictionary(i => i.Title);
            Assert.Equal((carol.Id, carol.Id), (items["Alice's"].OwnerId, items["Alice's"].CreatedById));
            Assert.Equal((bob.Id, carol.Id), (items["By Alice on Bob's"].OwnerId, items["By Alice on Bob's"].CreatedById));
        }

        await db.Get<IUserDataRepository>().DeleteUserAsync(carol.Id, null, default); // purge: her vehicles and what she made on Bob's
        await using var after = await Context(db);
        Assert.Empty(after.RecurringExpenses);
    }

    [Fact]
    public async Task Complete_SavesTheMovedBaselinesAndTheLinksTogether_AndTheLinksListTheCoveredSchedules()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        var oil = Item(car, "Oil");
        var filter = Item(car, "Filter");
        var other = Item(car, "Not done");
        foreach (var i in new[] { oil, filter, other }) await repo.AddAsync(i, default);
        var expense = Guid.NewGuid();

        var loaded = await repo.FindManyAsync([oil.Id, filter.Id, Guid.NewGuid()], default);
        Assert.Equal(new[] { oil.Id, filter.Id }.Order(), loaded.Select(i => i.Id).Order()); // an unknown id is simply absent
        foreach (var i in loaded) i.MarkDone(new DateOnly(2026, 12, 1), 61000);
        await repo.CompleteAsync(loaded, loaded.Select(i => RecurringCompletion.Create(expense, i.Id)).ToList(), default);

        Assert.Equal(new DateOnly(2026, 12, 1), (await repo.FindAsync(oil.Id, default))!.LastDoneDate);
        Assert.Equal(new DateOnly(2026, 12, 1), (await repo.FindAsync(filter.Id, default))!.LastDoneDate);
        Assert.Equal(Day, (await repo.FindAsync(other.Id, default))!.LastDoneDate);
        var covered = await repo.ListCompletionsForExpensesAsync([expense, Guid.NewGuid()], default);
        Assert.Equal(["Filter", "Oil"], covered.Select(c => c.Title)); // by title
        Assert.All(covered, c => Assert.Equal(expense, c.ExpenseId));
    }

    [Fact]
    public async Task Complete_StoresNothing_WhenTheSaveFails()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        var oil = Item(car, "Oil");
        await repo.AddAsync(oil, default);
        var moved = (await repo.FindAsync(oil.Id, default))!;
        moved.MarkDone(new DateOnly(2026, 12, 1), 61000);
        var expense = Guid.NewGuid();

        // A link to a schedule that does not exist breaks the foreign key: the whole save is refused.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => repo.CompleteAsync([moved], [RecurringCompletion.Create(expense, oil.Id), RecurringCompletion.Create(expense, Guid.NewGuid())], default));

        Assert.Equal(Day, (await repo.FindAsync(oil.Id, default))!.LastDoneDate);
        Assert.Empty(await repo.ListCompletionsForExpensesAsync([expense], default));
    }

    [Fact]
    public async Task TheLinks_GoWithTheirSchedule_WhenItIsDeleted_TheVehiclePurged_OrItsCreatorPurged()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var alice = User.CreateLocal("alice@x.co", null, false);
        await users.AddAsync(alice, default);
        var car = await AddVehicle(db, alice.Id);
        var van = await AddVehicle(db);
        var repo = db.Get<IRecurringExpenseRepository>();
        var deleted = Item(car, "Deleted");
        var onCar = Item(car, "On the car");
        var onVan = Item(van, "By Alice on the van", createdBy: alice.Id);
        foreach (var i in new[] { deleted, onCar, onVan }) await repo.AddAsync(i, default);
        await repo.CompleteAsync([], new[] { deleted, onCar, onVan }.Select(i => RecurringCompletion.Create(Guid.NewGuid(), i.Id)).ToList(), default);
        async Task<int> Links() { await using var ctx = await Context(db); return await ctx.RecurringCompletions.CountAsync(); }
        Assert.Equal(3, await Links());

        await repo.RemoveAsync(deleted, default);
        Assert.Equal(2, await Links());

        await db.Get<IUserDataRepository>().DeleteUserAsync(alice.Id, null, default); // purges her car and what she made on the van
        Assert.Equal(0, await Links());
    }
}

