using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Imports;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Runs;
using Tankstat.Application.Sharing;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Backups.Reader;
using Tankstat.Infrastructure.Imports;
using Tankstat.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// An import from a backup, from the opened file to the cars (#337), through the real services, the real run scope and the real queue:
/// on SQLite always, and on the database servers the environment names (docs/development.md). The backup is written by another
/// instance of the app, so every id in it is foreign here.
/// </summary>
public sealed partial class BackupImportRunTests(ITestOutputHelper output) : IAsyncDisposable
{
    private static readonly DateOnly Day = new(2026, 3, 1);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-import-run-{Guid.NewGuid():N}");
    private readonly List<TestDatabase> _databases = [];
    private readonly List<TestDatabase> _servers = [];
    private readonly SignedIn _user = new();
    private readonly AheadClock _clock = new();

    private sealed class SignedIn : ICurrentUser
    {
        public Principal? Principal { get; set; }
        public Task<Principal?> GetPrincipalAsync(CancellationToken ct) => Task.FromResult(Principal);
    }

    private sealed class AheadClock : TimeProvider
    {
        public TimeSpan Ahead { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Ahead;
    }

    public static TheoryData<string> Providers => new("Sqlite", "PostgreSql", "SqlServer");

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
            await using (var context = await server.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync()) await context.Database.EnsureDeletedAsync();
        foreach (var database in _databases)
        {
            await database.Get<BackupImportSessions>().StopAsync(default);
            await database.DisposeAsync();
        }
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TestDatabase? Instance(string provider, string name, TimeSpan? settle = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone",
            ["Storage:Path"] = Path.Combine(_root, name, "uploads"), ["DataProtection:KeysPath"] = Path.Combine(_root, name, "keys"), ["Backups:Path"] = Path.Combine(_root, name, "backups"),
            ["Imports:ScratchFolder"] = Path.Combine(_root, name, "scratch"),
        };
        if (provider != "Sqlite")
        {
            if (Environment.GetEnvironmentVariable($"TANKSTAT_TEST_{provider.ToUpperInvariant()}") is not { Length: > 0 } template)
            {
                output.WriteLine($"{provider}: not run, TANKSTAT_TEST_{provider.ToUpperInvariant()} is not set.");
                return null;
            }
            settings["Database:Provider"] = provider;
            settings["Database:ConnectionString"] = template.Replace("{db}", $"tankstat_imp_{Guid.NewGuid():N}"[..40]);
        }
        var database = new TestDatabase(settings, services => services.AddSingleton<ICurrentUser>(_user).AddSingleton<TimeProvider>(_clock)
            .AddSingleton(new RunTimings { Settle = settle ?? TimeSpan.Zero, BusyWait = TimeSpan.FromSeconds(1) }));
        _databases.Add(database);
        if (provider != "Sqlite") _servers.Add(database);
        return database;
    }

    private static async Task<Principal> PersonAsync(TestDatabase database, string name, bool admin = false)
    {
        var user = User.CreateLocal($"{name.ToLowerInvariant()}@example.com", name, admin);
        await database.Get<IUserRepository>().AddAsync(user, default);
        return new Principal(user.Id, user.DisplayName, user.Email, user.IsAdmin);
    }

    /// <summary>Something done as a person, through the services of a request of theirs.</summary>
    private async Task<T> AsAsync<T>(TestDatabase database, Principal who, Func<IServiceProvider, Task<T>> act)
    {
        _user.Principal = who;
        await using var scope = database.Services.CreateAsyncScope();
        return await act(scope.ServiceProvider);
    }

    private sealed record Source(string Backup, Guid Octavia, Guid Fabia);

    /// <summary>Another instance with Anna's two cars, and its backup: an Octavia with two fill-ups, a wash and an oil-change schedule, a Fabia with one fill-up.</summary>
    private async Task<Source> SourceAsync()
    {
        var source = Instance("Sqlite", "source")!;
        var anna = await PersonAsync(source, "Anna");
        var (octavia, fabia) = await AsAsync(source, anna, async sp =>
        {
            var vehicles = sp.GetRequiredService<VehicleService>();
            var octavia = (await vehicles.AddAsync("Octavia", "ABC-123", FuelType.Diesel, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            await fills.LogAsync(octavia, new(Day, 40, 60, "EUR", 10_000, true, "first"), default);
            await fills.LogAsync(octavia, new(Day.AddDays(10), 38, 57, "EUR", 10_650, true, "second"), default);
            await sp.GetRequiredService<ExpenseService>().AddAsync(octavia, new(Day.AddDays(2), "Car wash", "Care", 12, "EUR", null, null), default);
            await sp.GetRequiredService<RecurringExpenseService>().AddAsync(octavia, new("Oil change", "Service", null, RecurrenceKind.Time, 12, null, Day.AddYears(-1), null, null, null), default);
            var fabia = (await vehicles.AddAsync("Fabia", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await fills.LogAsync(fabia, new(Day.AddDays(1), 30, 45, "EUR", 55_000, true, null), default);
            return (octavia, fabia);
        });
        var backup = await source.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return new Source(backup.Path, octavia, fabia);
    }

    private async Task<string> OpenAsync(TestDatabase database, Principal who, string backup)
    {
        await using var content = File.OpenRead(backup);
        var status = await database.Get<IBackupImportSessions>().UploadAsync(who.Id, content, default);
        for (var i = 0; i < 600 && database.Get<IBackupImportSessions>().Find(who.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        Assert.Equal(BackupImportState.Opened, database.Get<IBackupImportSessions>().Find(who.Id, status.Token)!.State);
        return status.Token;
    }

    /// <summary>The queue looks, as its timer does, until the run is over; the run's row as it ended.</summary>
    private static async Task<ImportRun> FinishedAsync(TestDatabase database, Guid runId)
    {
        var runner = database.Get<BackupImportRunner>();
        for (var i = 0; i < 1200; i++)
        {
            await runner.TickAsync(default);
            var run = (await database.Get<IImportRunRepository>().FindAsync(runId, default))!;
            if (!run.IsActive) return run;
            await Task.Delay(50);
        }
        throw new TimeoutException("The run did not end.");
    }

    private Task<ImportRunView> StartAsync(TestDatabase database, Principal who, string token, params ImportCarChoice[] choices) =>
        AsAsync(database, who, sp => sp.GetRequiredService<BackupImportRunService>().StartAsync(token, choices, default));

    /// <summary>
    /// A person's notifications once at least <paramref name="count"/> are there: a run tells people after its row says how it ended (after
    /// the commit, or after its failure is written), so a test that saw the row may be a moment ahead of them.
    /// </summary>
    private static async Task<List<Notification>> ToldAsync(TestDatabase database, Guid recipient, int count = 1)
    {
        for (var i = 0; ; i++)
        {
            var told = await NotificationsAsync(database, recipient);
            if (told.Count >= count || i == 250) return told;
            await Task.Delay(20);
        }
    }

    private static async Task<List<Notification>> NotificationsAsync(TestDatabase database, Guid recipient)
    {
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.Notifications.AsNoTracking().Where(n => n.RecipientId == recipient).OrderBy(n => n.CreatedAt).ToListAsync();
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarOfTheBackup_ComesInAsANewCarOfTheImporters_WithNewIds_AndASecondImportIntoItChangesNothing(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var admin = await PersonAsync(here, "Root", admin: true);
        var token = await OpenAsync(here, bob, source.Backup);

        var offers = await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().OffersAsync(token, default));
        Assert.Equal(["Fabia", "Octavia"], offers.Cars.Select(c => c.Car.Name));
        Assert.All(offers.Cars, c => Assert.Equal((false, "Anna", (Guid?)null), (c.OwnedByMe, c.Car.OwnerName, c.SuggestedTargetId))); // was Anna's; Bob has no car to suggest

        var queued = await StartAsync(here, bob, token, new ImportCarChoice(source.Octavia, null));
        Assert.Equal((ImportRunState.Waiting, 0, 1, 4), (queued.State, queued.Ahead, queued.Plan.Cars.Count, queued.Progress.Total));
        var done = await FinishedAsync(here, queued.Id);

        Assert.Equal((ImportRunState.Done, null), (done.State, done.Failure));
        var report = ImportRunJson.Read<ImportRunReport>(done.Report)!;
        var car = Assert.Single(report.Cars);
        Assert.Equal((true, false, 2, 1, 1, 0, 0), (car.AsNew, car.LeftOut, car.Refuelings, car.Expenses, car.Schedules, car.Existing, car.Refused.Count));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var vehicle = await db.Vehicles.SingleAsync();
            Assert.Equal((car.VehicleId, bob.Id, "Octavia", "ABC-123", FuelType.Diesel), (vehicle.Id, vehicle.OwnerId, vehicle.Name, vehicle.LicensePlate, vehicle.FuelType));
            Assert.NotEqual(source.Octavia, vehicle.Id); // new ids: nothing of the other instance's is kept
            var fills = await db.Refuelings.Include(r => r.OdometerReading).OrderBy(r => r.Date).ToListAsync();
            Assert.Equal([(Day, 10_000L, "first", bob.Id), (Day.AddDays(10), 10_650L, "second", bob.Id)], fills.Select(r => (r.Date, r.Odometer!.Value, r.Note!, r.CreatedById)));
            Assert.NotNull(fills[1].Consumption); // worked out once for the car, after its fill-ups
            Assert.Equal(("Car wash", "Care"), await db.Expenses.Select(e => new ValueTuple<string, string?>(e.Title, e.Category)).SingleAsync());
            Assert.Equal(("Oil change", bob.Id), await db.RecurringExpenses.Select(s => new ValueTuple<string, Guid>(s.Title, s.OwnerId)).SingleAsync());
            Assert.Equal(0, await db.RunMarkers.CountAsync(m => m.EndedAt == null));
        }
        // Who is told, after the commit: the importer, and the administrators with the importer as who did it.
        var bobs = Assert.Single(await ToldAsync(here, bob.Id));
        Assert.Equal((NotificationKind.ImportFinished, queued.Id, "1", "Octavia", "0"), (bobs.Kind, bobs.SubjectId, bobs.Args["count"], bobs.Args["names"], bobs.Args["refused"]));
        var roots = Assert.Single(await ToldAsync(here, admin.Id));
        Assert.Equal((NotificationKind.CarsImported, "Bob", "Octavia"), (roots.Kind, roots.Args["actor"], roots.Args["names"]));
        Assert.Null(here.Get<IBackupImportSessions>().Find(bob.Id, token)); // the file is done with
        Assert.Contains(here.Log.Entries, e => e.Message.Contains(queued.Id.ToString()) && e.Message.Contains("imported 1 cars"));
        Assert.False(here.Log.Mentions("Octavia")); // ids and counts only

        // The same file into that car again: everything is there already.
        var again = await OpenAsync(here, bob, source.Backup);
        var suggested = await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().OffersAsync(again, default));
        Assert.Equal(car.VehicleId, suggested.Cars.Single(c => c.Car.Name == "Octavia").SuggestedTargetId); // the same plate
        var review = Assert.Single(await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(again, [new(source.Octavia, car.VehicleId)], default)));
        Assert.Equal((0, 0, 0, 2, 1, 1, 0, 0), (review.NewRefuelings, review.NewExpenses, review.NewSchedules, review.ExistingRefuelings, review.ExistingExpenses, review.ExistingSchedules, review.Differing, review.InTrash));
        var second = await FinishedAsync(here, (await StartAsync(here, bob, again, new ImportCarChoice(source.Octavia, car.VehicleId))).Id);
        Assert.Equal(ImportRunState.Done, second.State);
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.Equal((1, 2, 1, 1), (await db.Vehicles.CountAsync(), await db.Refuelings.CountAsync(), await db.Expenses.CountAsync(), await db.RecurringExpenses.CountAsync()));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task IntoAnOwnCar_OnlyTheMissingComeIn_WhatIsInItsTrashIsLeft_AndOnlyACarThePersonOwnsIsATarget(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var carol = await PersonAsync(here, "Carol");
        var admin = await PersonAsync(here, "Root", admin: true);
        // Bob's own Octavia has the first fill-up already (another id, the same day and reading), the second one in its trash, and no wash.
        var (mine, carols) = await AsAsync(here, bob, async sp =>
        {
            var id = (await sp.GetRequiredService<VehicleService>().AddAsync("My Octavia", "abc 123", FuelType.Diesel, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            await fills.LogAsync(id, new(Day, 40, 60, "EUR", 10_000, true, "first"), default);
            var trashed = await fills.LogAsync(id, new(Day.AddDays(10), 38, 57, "EUR", 10_650, true, "second"), default);
            await fills.DeleteAsync(trashed.Id, default);
            return (id, Guid.Empty);
        });
        carols = await AsAsync(here, carol, async sp =>
        {
            var id = (await sp.GetRequiredService<VehicleService>().AddAsync("Carol's", null, FuelType.Diesel, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<ResourceSharingService>().SetLogAccessAsync(id, bob.Id, AccessLevel.Edit, default); // Bob may edit its logs
            return id;
        });
        var token = await OpenAsync(here, bob, source.Backup);

        var offers = await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().OffersAsync(token, default));
        Assert.Equal([mine], offers.Targets.Select(t => t.Id)); // not Carol's, although he may edit it
        Assert.Equal(mine, offers.Cars.Single(c => c.Car.Name == "Octavia").SuggestedTargetId);
        var run = AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(source.Octavia, carols)], default));
        Assert.Equal("vehicle.notFound", (await Assert.ThrowsAsync<NotFoundException>(() => run)).Key);
        // An administrator passes every edit check, and still imports only into a car of their own.
        var adminToken = await OpenAsync(here, admin, source.Backup);
        await Assert.ThrowsAsync<NotFoundException>(() => StartAsync(here, admin, adminToken, new ImportCarChoice(source.Octavia, mine)));

        var review = Assert.Single(await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(source.Octavia, mine)], default)));
        Assert.Equal((0, 1, 1, 1, 0, 0, 0, 1, "My Octavia"), (review.NewRefuelings, review.NewExpenses, review.NewSchedules, review.ExistingRefuelings, review.ExistingExpenses, review.ExistingSchedules, review.Differing, review.InTrash, review.TargetName));
        var done = await FinishedAsync(here, (await StartAsync(here, bob, token, new ImportCarChoice(source.Octavia, mine))).Id);

        var car = Assert.Single(ImportRunJson.Read<ImportRunReport>(done.Report)!.Cars);
        Assert.Equal((false, mine, "My Octavia", 0, 1, 1, 1, 1), (car.AsNew, car.VehicleId!.Value, car.TargetName, car.Refuelings, car.Expenses, car.Schedules, car.Existing, car.InTrash));
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal((1, 1, 1), (await db.Refuelings.CountAsync(r => r.VehicleId == mine), await db.Expenses.CountAsync(e => e.VehicleId == mine), await db.RecurringExpenses.CountAsync(s => s.VehicleId == mine)));
        Assert.Equal(1, await db.Refuelings.IgnoreQueryFilters().CountAsync(r => r.VehicleId == mine && r.DeletedAt != null)); // still in the trash, said and left
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ARefusedEntry_FollowsItsCarsPolicy_TheRestOfTheCarOrNoneOfIt_AndTheOtherCarsComeInEitherWay(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var bob = await PersonAsync(here, "Bob");
        // Bob's car already stands at a reading the backup's second fill-up contradicts: 10,650 on a day after 20,000.
        async Task<Guid> OwnAsync(string name) => await AsAsync(here, bob, async sp =>
        {
            var id = (await sp.GetRequiredService<VehicleService>().AddAsync(name, null, FuelType.Diesel, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<RefuelingService>().LogAsync(id, new(Day.AddDays(5), 41, 62, "EUR", 20_000, true, null), default);
            return id;
        });
        var (rest, leftOut) = (await OwnAsync("Takes the rest"), await OwnAsync("Left out"));

        async Task<ImportRunReport> ImportAsync(Guid target, RefusedEntryPolicy policy)
        {
            var token = await OpenAsync(here, bob, source.Backup);
            var done = await FinishedAsync(here, (await StartAsync(here, bob, token, new ImportCarChoice(source.Octavia, target, policy), new ImportCarChoice(source.Fabia, null))).Id);
            Assert.Equal(ImportRunState.Done, done.State);
            return ImportRunJson.Read<ImportRunReport>(done.Report)!;
        }

        var first = await ImportAsync(rest, RefusedEntryPolicy.ImportTheRest);
        var octavia = first.Cars.Single(c => c.CarId == source.Octavia);
        Assert.Equal((false, 1, 1, 1), (octavia.LeftOut, octavia.Refuelings, octavia.Expenses, octavia.Schedules)); // the first fill-up fits before the car's own
        var refused = Assert.Single(octavia.Refused); // the second one contradicts the car's reading
        Assert.Equal(("refueling", Day.AddDays(10), "odometer."), (refused.Kind, refused.Date!.Value, refused.Key[..9]));
        Assert.Equal((false, 1), (first.Cars.Single(c => c.CarId == source.Fabia).LeftOut, first.Cars.Single(c => c.CarId == source.Fabia).Refuelings));

        var second = await ImportAsync(leftOut, RefusedEntryPolicy.LeaveCarOut);
        var left = second.Cars.Single(c => c.CarId == source.Octavia);
        Assert.Equal((true, 0, 0, 0, 1), (left.LeftOut, left.Refuelings, left.Expenses, left.Schedules, left.Refused.Count)); // the entry that left it out
        Assert.False(second.Cars.Single(c => c.CarId == source.Fabia).LeftOut);
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal((1, 0, 0), (await db.Refuelings.CountAsync(r => r.VehicleId == leftOut), // its own fill-up only: the backup's first one went with the car
            await db.Expenses.CountAsync(e => e.VehicleId == leftOut), await db.RecurringExpenses.CountAsync(s => s.VehicleId == leftOut)));
        Assert.Equal(2, await db.Vehicles.CountAsync(v => v.Name == "Fabia")); // each run's Fabia came in
        var told = (await ToldAsync(here, bob.Id, 2)).Where(n => n.Kind == NotificationKind.ImportFinished).ToList();
        Assert.Equal(["2", "1"], told.Select(n => n.Args["count"])); // the cars that came in
    }

    private sealed class BreaksAtTheSecondCar(IBackupCars inner) : IBackupCars
    {
        private int _read;
        public BackupFileSummary Summary => inner.Summary;
        public Task<BackupCar?> ReadCarAsync(Guid carId, CancellationToken ct) =>
            ++_read > 1 ? throw new InvalidOperationException("Zzcanary: the disk went away") : inner.ReadCarAsync(carId, ct);
        public Task<Tankstat.Application.Tyres.TyreBook?> ReadTyresAsync(Guid carId, CancellationToken ct) => inner.ReadTyresAsync(carId, ct);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task SomethingUnexpectedAfterTheFirstCar_LeavesNothingOfTheRun(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var token = await OpenAsync(here, bob, source.Backup);
        var file = here.Get<IBackupImportSessions>().Cars(bob.Id, token)!;
        var plan = new ImportRunPlan([new(source.Octavia, "Octavia", null, null, RefusedEntryPolicy.ImportTheRest, 4), new(source.Fabia, "Fabia", null, null, RefusedEntryPolicy.ImportTheRest, 1)]);

        await using var scope = here.Services.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<BackupImportWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => here.Get<IRunScope>().RunAsync(new RunStart(bob, []),
            run => work.RunAsync(run, bob, new BreaksAtTheSecondCar(file), plan, _ => { }, default), default));

        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal((0, 0, 0, 0), (await db.Vehicles.CountAsync(), await db.Refuelings.CountAsync(), await db.Expenses.CountAsync(), await db.RecurringExpenses.CountAsync()));
    }

    /// <summary>
    /// A run may wait half an hour for its turn, and its target can go to the trash meanwhile. That is a refusal of the car, not a failure
    /// of the run: the car is left out with the reason, whatever its policy, and the other cars come in.
    /// </summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ACarWhoseTargetWentToTheTrashSinceTheReview_IsLeftOutWithTheReason_AndTheOtherCarsComeIn(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var mine = await AsAsync(here, bob, async sp => (await sp.GetRequiredService<VehicleService>().AddAsync("Mine", null, FuelType.Diesel, MeasurementUnits.Metric, default)).Id);
        var token = await OpenAsync(here, bob, source.Backup);
        var file = here.Get<IBackupImportSessions>().Cars(bob.Id, token)!;
        // The plan as the review made it, with the target still there...
        var plan = new ImportRunPlan([new(source.Octavia, "Octavia", mine, "Mine", RefusedEntryPolicy.ImportTheRest, 4), new(source.Fabia, "Fabia", null, null, RefusedEntryPolicy.ImportTheRest, 1)]);
        // ...which goes to the trash before the run gets its turn.
        await AsAsync(here, bob, async sp => (await sp.GetRequiredService<VehicleService>().DeleteAsync(mine, default)).Id);

        await using var scope = here.Services.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<BackupImportWork>();
        ImportRunReport? report = null;
        await here.Get<IRunScope>().RunAsync(new RunStart(bob, [mine]), async run => report = await work.RunAsync(run, bob, file, plan, _ => { }, default), default);

        var octavia = report!.Cars.Single(c => c.CarId == source.Octavia);
        var refused = Assert.Single(octavia.Refused);
        Assert.Equal((true, ImportRefusedEntry.Car, "vehicle.notFound"), (octavia.LeftOut, refused.Kind, refused.Key));
        Assert.False(report.Cars.Single(c => c.CarId == source.Fabia).LeftOut);
        Assert.Equal((1, 0), (report.Imported, report.Refused)); // the car's own refusal is no refused entry
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(["Fabia"], await db.Vehicles.Select(v => v.Name).ToListAsync());
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ASecondRun_WaitsInLine_AndStartsByItself_AndOneThatWaitsTooLongGivesUp(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var (bob, carol, dave) = (await PersonAsync(here, "Bob"), await PersonAsync(here, "Carol"), await PersonAsync(here, "Dave"));
        var first = await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), new ImportCarChoice(source.Octavia, null));
        var second = await StartAsync(here, carol, await OpenAsync(here, carol, source.Backup), new ImportCarChoice(source.Fabia, null));

        Assert.Equal(1, (await AsAsync(here, carol, sp => sp.GetRequiredService<BackupImportRunService>().MineAsync(default)))!.Ahead);
        // One run per person at a time.
        var bobsOther = await OpenAsyncKeeping(here, bob, source.Backup);
        Assert.Equal("import.fileInUse", bobsOther);

        Assert.Equal(ImportRunState.Done, (await FinishedAsync(here, first.Id)).State);
        Assert.Equal(ImportRunState.Done, (await FinishedAsync(here, second.Id)).State); // by itself: nobody pressed anything
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.Equal(["Fabia", "Octavia"], await db.Vehicles.OrderBy(v => v.Name).Select(v => v.Name).ToListAsync());

        // Half an hour in line is the most: then it gives up, says so, and nothing of it came in.
        var third = await StartAsync(here, dave, await OpenAsync(here, dave, source.Backup), new ImportCarChoice(source.Fabia, null));
        _clock.Ahead = TimeSpan.FromMinutes(31);
        var gaveUp = await FinishedAsync(here, third.Id);
        Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.DidNotStart), (gaveUp.State, gaveUp.Failure));
        var told = Assert.Single(await ToldAsync(here, dave.Id));
        Assert.Equal((NotificationKind.ImportNotFinished, "DidNotStart"), (told.Kind, told.Args["reason"]));
    }

    /// <summary>A second upload of someone whose run is going: refused, with the key it is refused by.</summary>
    private async Task<string> OpenAsyncKeeping(TestDatabase database, Principal who, string backup)
    {
        await using var content = File.OpenRead(backup);
        return (await Assert.ThrowsAsync<DomainException>(() => database.Get<IBackupImportSessions>().UploadAsync(who.Id, content, default))).Key;
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACancel_ThatCameBeforeTheRunsMarkExisted_IsKeptAndHonouredBeforeAnythingIsSaved(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var (bob, admin) = (await PersonAsync(here, "Bob"), await PersonAsync(here, "Root", admin: true));
        var run = await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), new ImportCarChoice(source.Octavia, null));

        // What the cancel leaves when it finds no mark to set (the run's turn came a moment ago): the request, kept by the runner.
        here.Get<BackupImportRunner>().Cancelling(run.Id, admin.Id);
        await here.Get<BackupImportRunner>().TickAsync(default);

        var ended = await FinishedAsync(here, run.Id);
        Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.Cancelled, admin.Id), (ended.State, ended.Failure, ended.CancelledById));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.Equal((0, 0), (await db.Vehicles.CountAsync(), await db.RunMarkers.CountAsync(m => m.EndedAt == null)));
        var told = Assert.Single(await ToldAsync(here, bob.Id));
        Assert.Equal((NotificationKind.ImportCancelled, "Root"), (told.Kind, told.Args["actor"]));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACancel_ByTheStarterOrAnAdministrator_LeavesNothing_WhetherTheRunWaitsOrGoes_AndTheStarterIsToldWhenItWasSomeoneElse(string provider)
    {
        if (Instance(provider, "here", settle: TimeSpan.FromMilliseconds(800)) is not { } here) return;
        var source = await SourceAsync();
        var (bob, carol, eve) = (await PersonAsync(here, "Bob"), await PersonAsync(here, "Carol"), await PersonAsync(here, "Eve"));
        var admin = await PersonAsync(here, "Root", admin: true);
        var running = await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), new ImportCarChoice(source.Octavia, null));
        var waiting = await StartAsync(here, carol, await OpenAsync(here, carol, source.Backup), new ImportCarChoice(source.Fabia, null));
        Task<bool> Cancel(Principal who, Guid id) => AsAsync(here, who, sp => sp.GetRequiredService<BackupImportRunService>().CancelAsync(id, default));

        // Someone else who is no administrator cancels nothing, and sees no run but their own.
        Assert.False(await Cancel(eve, waiting.Id));
        Assert.Null(await AsAsync(here, eve, sp => sp.GetRequiredService<BackupImportRunService>().FindAsync(waiting.Id, default)));
        Assert.Equal(2, (await AsAsync(here, admin, sp => sp.GetRequiredService<BackupImportRunService>().ActiveAsync(default))).Count);

        // The first run gets its turn and is cancelled by the administrator while it settles, before a row is saved.
        await here.Get<BackupImportRunner>().TickAsync(default);
        for (var i = 0; i < 200 && await here.Get<IRunMarkerRepository>().FindAsync(running.Id, default) is null; i++) await Task.Delay(10);
        Assert.True(await Cancel(admin, running.Id));
        // The waiting one is cancelled by its starter.
        Assert.True(await Cancel(carol, waiting.Id));

        var (bobs, carols) = (await FinishedAsync(here, running.Id), await FinishedAsync(here, waiting.Id));
        Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.Cancelled, admin.Id), (bobs.State, bobs.Failure, bobs.CancelledById));
        Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.Cancelled, carol.Id), (carols.State, carols.Failure, carols.CancelledById));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.Equal((0, 0), (await db.Vehicles.CountAsync(), await db.RunMarkers.CountAsync(m => m.EndedAt == null)));
        var told = Assert.Single(await ToldAsync(here, bob.Id));
        Assert.Equal((NotificationKind.ImportCancelled, "Root"), (told.Kind, told.Args["actor"]));
        Assert.Empty(await NotificationsAsync(here, carol.Id)); // she cancelled it herself
        Assert.False(await Cancel(bob, running.Id)); // over already
        // The file is still open for its half hour: back to the review.
        Assert.True((await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().FindAsync(running.Id, default)))!.FileKept);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ARunOfAServerThatIsGone_EndsAsNotFinished_AndItsStarterIsTold(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var bob = await PersonAsync(here, "Bob");
        var runs = here.Get<IImportRunRepository>();
        var now = _clock.GetUtcNow();
        // Rows of a server process that stopped: one that waited, one that ran (its mark was ended by the start's sweep, or never written).
        var waited = ImportRun.Queue(Guid.NewGuid(), ImportRunKind.Import, bob.Id, "a-server-that-is-gone", "{\"cars\":[]}", 1, 3, now - TimeSpan.FromMinutes(5));
        var ran = ImportRun.Queue(Guid.NewGuid(), ImportRunKind.Import, bob.Id, "a-server-that-is-gone", "{\"cars\":[]}", 1, 3, now - TimeSpan.FromMinutes(6));
        await runs.AddAsync(ran, default);
        Assert.True(await runs.ClaimAsync(ran.Id, now - TimeSpan.FromMinutes(6), default));
        await runs.AddAsync(waited, default);
        Assert.False(await runs.ClaimAsync(waited.Id, now, default)); // one runs: it is nobody else's turn

        await here.Get<BackupImportRunner>().TickAsync(default);

        Assert.All(new[] { waited.Id, ran.Id }, id => Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.Interrupted), (runs.FindAsync(id, default).Result!.State, runs.FindAsync(id, default).Result!.Failure)));
        Assert.Equal([NotificationKind.ImportNotFinished, NotificationKind.ImportNotFinished], (await NotificationsAsync(here, bob.Id)).Select(n => n.Kind));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ARunGivenItsTurnSinceItWasSeenWaiting_IsNotEndedAsAWaitingOne_AndOneEndedFromOutsideIsNotCompleted(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var bob = await PersonAsync(here, "Bob");
        var runs = here.Get<IImportRunRepository>();
        var now = _clock.GetUtcNow();
        var run = ImportRun.Queue(Guid.NewGuid(), ImportRunKind.Import, bob.Id, "another-server", "{\"cars\":[]}", 1, 3, now);
        await runs.AddAsync(run, default);
        Assert.True(await runs.ClaimAsync(run.Id, now, default)); // its turn came after a cancel (or the wait's limit) read it as waiting

        // What ends a run because it waits finds it running: nothing is written, and the cancel goes on to tell the run instead.
        Assert.False(await runs.FailAsync(run.Id, ImportRunFailure.Cancelled, bob.Id, now, default, waitingOnly: true));
        Assert.Equal((ImportRunState.Running, null), ((await runs.FindAsync(run.Id, default))!.State, (await runs.FindAsync(run.Id, default))!.Failure));

        // Ended from outside while it ran (its starter was told so): its last save refuses, so the run rolls back with it.
        Assert.True(await runs.FailAsync(run.Id, ImportRunFailure.Interrupted, null, now, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.CompleteAsync(run.Id, "{\"cars\":[]}", 1, 3, now, default));
        Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.Interrupted), ((await runs.FindAsync(run.Id, default))!.State, (await runs.FindAsync(run.Id, default))!.Failure));
    }

    [Fact]
    public async Task OnMySql_NoRunIsOffered_UntilItCanReadItsDates()
    {
        // #334's T14 and #345: the key the start is refused with there; nothing is queued.
        var here = Instance("Sqlite", "here")!;
        Assert.Null(here.Get<IBackupImportRunner>().UnavailableKey);
        Assert.Equal("import.notOnMySql", BackupImportRunner.NotOnMySqlKey);
        await Task.CompletedTask;
    }
}
