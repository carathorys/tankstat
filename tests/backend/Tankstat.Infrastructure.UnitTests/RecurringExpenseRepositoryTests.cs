using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
}
