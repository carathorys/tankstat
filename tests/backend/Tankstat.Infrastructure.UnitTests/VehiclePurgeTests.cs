using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Photos;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Tyres;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// What an import's <i>Replace all</i> stands on (#338): every log of one kind of one car deleted for good, and a visit's link added
/// without its schedule moving. Through the real services, on SQLite always and on the database servers the environment names.
/// </summary>
public sealed class VehiclePurgeTests(ITestOutputHelper output) : IAsyncDisposable
{
    private static readonly DateOnly Day = new(2026, 3, 1);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-purge-{Guid.NewGuid():N}");
    private readonly List<TestDatabase> _databases = [];
    private readonly List<TestDatabase> _servers = [];
    private readonly SignedIn _user = new();

    private sealed class SignedIn : ICurrentUser
    {
        public Principal? Principal { get; set; }
        public Task<Principal?> GetPrincipalAsync(CancellationToken ct) => Task.FromResult(Principal);
    }

    public static TheoryData<string> Providers => new("Sqlite", "PostgreSql", "SqlServer");

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
            await using (var context = await server.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync()) await context.Database.EnsureDeletedAsync();
        foreach (var database in _databases) await database.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TestDatabase? Instance(string provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone",
            ["Storage:Path"] = Path.Combine(_root, "uploads"), ["DataProtection:KeysPath"] = Path.Combine(_root, "keys"), ["Backups:Path"] = Path.Combine(_root, "backups"),
            ["Imports:ScratchFolder"] = Path.Combine(_root, "scratch"),
        };
        if (provider != "Sqlite")
        {
            if (Environment.GetEnvironmentVariable($"TANKSTAT_TEST_{provider.ToUpperInvariant()}") is not { Length: > 0 } template)
            {
                output.WriteLine($"{provider}: not run, TANKSTAT_TEST_{provider.ToUpperInvariant()} is not set.");
                return null;
            }
            settings["Database:Provider"] = provider;
            settings["Database:ConnectionString"] = template.Replace("{db}", $"tankstat_prg_{Guid.NewGuid():N}"[..40]);
        }
        var database = new TestDatabase(settings, services => services.AddSingleton<ICurrentUser>(_user));
        _databases.Add(database);
        if (provider != "Sqlite") _servers.Add(database);
        return database;
    }

    private static async Task<Principal> PersonAsync(TestDatabase database, string name)
    {
        var user = User.CreateLocal($"{name.ToLowerInvariant()}@example.com", name, false);
        await database.Get<IUserRepository>().AddAsync(user, default);
        return new Principal(user.Id, user.DisplayName, user.Email, user.IsAdmin);
    }

    private async Task<T> AsAsync<T>(TestDatabase database, Principal who, Func<IServiceProvider, Task<T>> act)
    {
        _user.Principal = who;
        await using var scope = database.Services.CreateAsyncScope();
        return await act(scope.ServiceProvider);
    }

    private sealed record Car(Guid Id, Guid Live, Guid Trashed, Guid Wash, Guid TrashedExpense, Guid Schedule, Guid Other);

    /// <summary>
    /// Bob's Golf with two refuellings (one in the trash), two expenses (one in the trash), a schedule the wash marked done; and his Polo
    /// with a refuelling and an expense, which nothing here may touch.
    /// </summary>
    private async Task<Car> CarAsync(TestDatabase here, Principal bob) => await AsAsync(here, bob, async sp =>
    {
        var vehicles = sp.GetRequiredService<VehicleService>();
        var (fills, expenses, recurring) = (sp.GetRequiredService<RefuelingService>(), sp.GetRequiredService<ExpenseService>(), sp.GetRequiredService<RecurringExpenseService>());
        var golf = (await vehicles.AddAsync("Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
        var live = (await fills.LogAsync(golf, new(Day, 40, 60, "EUR", 1_000, true, null, null, 2), default)).Id;
        var trashed = (await fills.LogAsync(golf, new(Day.AddDays(9), 40, 60, "EUR", 3_000, true, null), default)).Id;
        await fills.DeleteAsync(trashed, default);
        var wash = (await expenses.AddAsync(golf, new(Day.AddDays(4), "Wash", null, 12, "EUR", 2_000, null), default)).Id;
        var gone = (await expenses.AddAsync(golf, new(Day.AddDays(5), "Wipers", null, 20, "EUR", null, null), default)).Id;
        await expenses.DeleteAsync(gone, default);
        var schedule = (await recurring.AddAsync(golf, new("Washing", null, null, RecurrenceKind.Time, 1, null, Day, null, null, null), default)).Item.Id;
        Assert.True(await recurring.LinkAsync(wash, schedule, default));
        var polo = (await vehicles.AddAsync("Polo", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
        await fills.LogAsync(polo, new(Day, 30, 45, "EUR", 500, true, null), default);
        await expenses.AddAsync(polo, new(Day, "Polish", null, 9, "EUR", 500, null), default);
        return new Car(golf, live, trashed, wash, gone, schedule, polo);
    });

    private static async Task<(int Refuelings, int Expenses, int Readings, int Costs, int Links)> RowsAsync(TestDatabase here, Guid vehicleId)
    {
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return (await db.Refuelings.IgnoreQueryFilters().CountAsync(r => r.VehicleId == vehicleId), await db.Expenses.IgnoreQueryFilters().CountAsync(e => e.VehicleId == vehicleId),
            await db.OdometerReadings.IgnoreQueryFilters().CountAsync(r => r.VehicleId == vehicleId), await db.Costs.IgnoreQueryFilters().CountAsync(c => c.VehicleId == vehicleId),
            await db.RecurringCompletions.CountAsync());
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarsRefuellings_LiveAndTrashed_GoForGood_WithTheirReadingsAndCosts_AndTheDevicesAreTold(string provider)
    {
        if (Instance(provider) is not { } here) return;
        var bob = await PersonAsync(here, "Bob");
        var car = await CarAsync(here, bob);
        Assert.Equal((2, 2, 3, 4, 1), await RowsAsync(here, car.Id));

        Assert.Equal(2, await AsAsync(here, bob, sp => sp.GetRequiredService<RefuelingService>().PurgeForVehicleAsync(car.Id, default)));

        Assert.Equal((0, 2, 1, 2, 1), await RowsAsync(here, car.Id)); // the expenses, their reading, costs and link stay
        Assert.Equal((1, 1, 2, 2, 1), await RowsAsync(here, car.Other)); // the other car is not touched
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(new[] { car.Live, car.Trashed }.Order(), (await db.Tombstones.Where(t => t.EntityType == OfflineEntityType.Refueling).Select(t => t.EntityId).ToListAsync()).Order());
        Assert.Equal(0, await AsAsync(here, bob, sp => sp.GetRequiredService<RefuelingService>().PurgeForVehicleAsync(car.Id, default))); // nothing left: nothing happens
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarsExpenses_LiveAndTrashed_GoForGood_WithTheirLinksToSchedules_WhichStay(string provider)
    {
        if (Instance(provider) is not { } here) return;
        var bob = await PersonAsync(here, "Bob");
        var car = await CarAsync(here, bob);

        Assert.Equal(2, await AsAsync(here, bob, sp => sp.GetRequiredService<ExpenseService>().PurgeForVehicleAsync(car.Id, default)));

        Assert.Equal((2, 0, 2, 2, 0), await RowsAsync(here, car.Id)); // the refuellings stay; the link went with its expense
        Assert.Equal((1, 1, 2, 2, 0), await RowsAsync(here, car.Other));
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(new[] { car.Wash, car.TrashedExpense }.Order(), (await db.Tombstones.Where(t => t.EntityType == OfflineEntityType.Expense).Select(t => t.EntityId).ToListAsync()).Order());
        Assert.Equal(Day, (await db.RecurringExpenses.SingleAsync(s => s.Id == car.Schedule)).LastDoneDate); // the schedule is as it was
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ATyreLogLine_HoldsTheExpensesBack_WhilePhotosGoWithTheirLogs(string provider)
    {
        if (Instance(provider) is not { } here) return;
        var bob = await PersonAsync(here, "Bob");
        var car = await CarAsync(here, bob);
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            // A photo on the refuelling in the trash, one on an expense, and a tyre log line that holds the other expense (the trashed one).
            db.LogPhotos.Add(LogPhoto.Create(bob.Id, car.Id, LogType.Refueling, car.Trashed, Guid.NewGuid(), bob.Id, DateTimeOffset.UtcNow));
            db.LogPhotos.Add(LogPhoto.Create(bob.Id, car.Id, LogType.Expense, car.Wash, Guid.NewGuid(), bob.Id, DateTimeOffset.UtcNow));
            var line = TyreEntry.Create(bob.Id, bob.Id, car.Id, TyreEntryKind.Other, null, Day.AddDays(5), null, "Wipers");
            line.LinkExpense(car.TrashedExpense);
            db.TyreEntries.Add(line);
            await db.SaveChangesAsync();
        }

        var expense = await Assert.ThrowsAsync<DomainException>(() => AsAsync(here, bob, sp => sp.GetRequiredService<ExpenseService>().PurgeForVehicleAsync(car.Id, default)));
        Assert.Equal((VehiclePurgeRefusals.HeldBackKey, 0, 1), (expense.Key, expense.Args["photos"], expense.Args["lines"]));
        Assert.Equal((2, 2, 3, 4, 1), await RowsAsync(here, car.Id)); // nothing deleted

        // A photo holds nothing back (#339): it goes with its refuelling, its row and (its image's row being none here) nothing else.
        Assert.Equal(2, await AsAsync(here, bob, sp => sp.GetRequiredService<RefuelingService>().PurgeForVehicleAsync(car.Id, default)));
        await using var after = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal((0, 1), (await after.LogPhotos.CountAsync(p => p.LogType == LogType.Refueling), await after.LogPhotos.CountAsync(p => p.LogType == LogType.Expense)));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task OnlyWhoMayDeleteTheCarsLogs_PurgesThem_AndACarThatIsNotSeenLooksMissing(string provider)
    {
        if (Instance(provider) is not { } here) return;
        var (bob, eve) = (await PersonAsync(here, "Bob"), await PersonAsync(here, "Eve"));
        var car = await CarAsync(here, bob);

        var unseen = await Assert.ThrowsAsync<NotFoundException>(() => AsAsync(here, eve, sp => sp.GetRequiredService<RefuelingService>().PurgeForVehicleAsync(car.Id, default)));
        Assert.Equal("vehicle.notFound", unseen.Key);
        // Shared for editing: she may trash, never delete for good.
        await AsAsync(here, bob, async sp =>
        {
            await sp.GetRequiredService<Tankstat.Application.Sharing.ResourceSharingService>().SetLogAccessAsync(car.Id, eve.Id, Tankstat.Domain.Access.AccessLevel.Edit, default);
            return 0;
        });
        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => AsAsync(here, eve, sp => sp.GetRequiredService<ExpenseService>().PurgeForVehicleAsync(car.Id, default)));
        Assert.Equal(VehiclePurgeRefusals.NoDeleteKey, refused.Key);
        Assert.Equal((2, 2, 3, 4, 1), await RowsAsync(here, car.Id));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task AVisitsLink_IsAddedWithoutMovingTheSchedule_OnceOnly_AndOnlyInsideOneCar(string provider)
    {
        if (Instance(provider) is not { } here) return;
        var bob = await PersonAsync(here, "Bob");
        var car = await CarAsync(here, bob); // links the wash to the schedule once already

        Assert.False(await AsAsync(here, bob, sp => sp.GetRequiredService<RecurringExpenseService>().LinkAsync(car.Wash, car.Schedule, default))); // there already
        var listed = await AsAsync(here, bob, sp => sp.GetRequiredService<RecurringExpenseService>().ListCompletionsForExpensesAsync([car.Wash], default));
        Assert.Equal(["Washing"], listed[car.Wash].Select(c => c.Title));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var schedule = await db.RecurringExpenses.SingleAsync(s => s.Id == car.Schedule);
            Assert.Equal((Day, 1), (schedule.LastDoneDate, schedule.Version)); // not moved, not saved again
            Assert.Equal(1, await db.RecurringCompletions.CountAsync());
        }
        // Another car's expense is no expense of this schedule's car.
        var polish = await AsAsync(here, bob, async sp => (await sp.GetRequiredService<ExpenseService>().ListAsync(car.Other, new ExpenseQuery(), default)).Single().Id);
        var other = await Assert.ThrowsAsync<NotFoundException>(() => AsAsync(here, bob, sp => sp.GetRequiredService<RecurringExpenseService>().LinkAsync(polish, car.Schedule, default)));
        Assert.Equal("expense.notFound", other.Key);
    }
}
