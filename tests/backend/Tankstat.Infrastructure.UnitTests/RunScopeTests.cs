using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Runs;
using Tankstat.Application.Tyres;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Runs;
using Tankstat.Domain.Tyres;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.Infrastructure.Persistence.Runs;
using Xunit.Abstractions;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// A run's one transaction across the app's real services (#335), on SQLite always and on the database servers the environment names
/// (as <see cref="ProviderRoundTripTests"/>; docs/development.md has the docker lines). SQLite alone would not show a missing
/// savepoint: only PostgreSQL refuses every statement after an error. MySQL runs the vehicles only until #345 is fixed (it cannot read
/// a date column, so no log can be saved through the services there).
/// </summary>
public sealed class RunScopeTests(ITestOutputHelper output) : IDisposable
{
    private static readonly Principal Starter = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Starter", "starter@example.com", false);
    private static readonly DateOnly Day = new(2026, 3, 1);

    private readonly List<TestDatabase> _databases = [];
    private readonly List<TestDatabase> _servers = [];
    private readonly SignedIn _user = new();
    private readonly AheadClock _clock = new();

    public static TheoryData<string> Providers => new("Sqlite", "PostgreSql", "SqlServer", "MySql");

    public void Dispose()
    {
        foreach (var server in _servers)
            using (var context = server.Get<IDbContextFactory<AppDbContext>>().CreateDbContext()) context.Database.EnsureDeleted();
        foreach (var database in _databases) database.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>A migrated database of the provider with the real services, or null (said in the output) when its server is not named.</summary>
    private TestDatabase? Database(string provider, TimeSpan settle = default)
    {
        // No pause before a run's transaction and a short wait at the gate: the tests have no saves in flight to wait out.
        var settings = new Dictionary<string, string?> { ["Auth:Mode"] = "Standalone" };
        if (provider != "Sqlite")
        {
            if (Environment.GetEnvironmentVariable($"TANKSTAT_TEST_{provider.ToUpperInvariant()}") is not { Length: > 0 } template)
            {
                output.WriteLine($"{provider}: not run, TANKSTAT_TEST_{provider.ToUpperInvariant()} is not set.");
                return null;
            }
            settings["Database:Provider"] = provider;
            settings["Database:ConnectionString"] = template.Replace("{db}", $"tankstat_run_{Guid.NewGuid():N}"[..40]);
        }
        // Nobody is signed in unless a test says so: outside a run the services refuse, inside one they act as its starter.
        var database = new TestDatabase(settings, services => services.AddSingleton<ICurrentUser>(_user)
            .AddSingleton(new RunTimings { Settle = settle, BusyWait = TimeSpan.FromSeconds(1) })
            .AddSingleton<TimeProvider>(_clock));
        _databases.Add(database);
        if (provider != "Sqlite") _servers.Add(database);
        return database;
    }

    /// <summary>The real clock, set ahead by a test (the run's limit).</summary>
    private sealed class AheadClock : TimeProvider
    {
        public TimeSpan Ahead { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Ahead;
    }

    private sealed class SignedIn : ICurrentUser
    {
        public Principal? Principal { get; set; }
        public Task<Principal?> GetPrincipalAsync(CancellationToken ct) => Task.FromResult(Principal);
    }

    /// <summary>Someone else's request while a run is open: its own services, outside the run (the execution context is not handed on).</summary>
    private static Task<T> ElsewhereAsync<T>(TestDatabase database, Func<IServiceProvider, Task<T>> act)
    {
        using var _ = ExecutionContext.SuppressFlow();
        return Task.Run(async () =>
        {
            await using var scope = database.Services.CreateAsyncScope();
            return await act(scope.ServiceProvider);
        });
    }

    private static async Task<RunMarker> OnlyMarkerAsync(TestDatabase database)
    {
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.RunMarkers.AsNoTracking().Include(m => m.Vehicles).SingleAsync();
    }

    private static Task RunAsync(TestDatabase database, Func<IRun, IServiceProvider, Task> work, params Guid[] vehicles) =>
        database.Get<IRunScope>().RunAsync(new RunStart(Starter, vehicles), async run =>
        {
            await using var scope = database.Services.CreateAsyncScope();
            await work(run, scope.ServiceProvider);
        }, default);

    /// <summary>A car with what a run saves for it: through every kind of inner transaction the services have.</summary>
    private static async Task<Guid> AddCarAsync(IServiceProvider services, string name, bool logs, Guid? id = null)
    {
        var vehicle = await services.GetRequiredService<VehicleService>().AddAsync(name, null, FuelType.Petrol, MeasurementUnits.Metric, default, id);
        if (!logs) return vehicle.Id;

        var refuelings = services.GetRequiredService<RefuelingService>();
        await refuelings.LogAsync(vehicle.Id, new RefuelingInput(Day, 40, 60, "EUR", 10_000, true, null), default);
        await refuelings.LogAsync(vehicle.Id, new RefuelingInput(Day.AddDays(10), 35, 55, "EUR", 10_600, true, null), default);

        var expenses = services.GetRequiredService<ExpenseService>();
        var expense = await expenses.AddAsync(vehicle.Id, new ExpenseInput(Day.AddDays(2), "Wash", null, 12, "EUR", 10_100, null), default);
        // The reading let go of: two saves held together (LinkedChangesExtensions).
        await expenses.UpdateAsync(expense.Id, new ExpenseInput(Day.AddDays(2), "Wash", null, 12, "EUR", null, null), default);

        var recurring = services.GetRequiredService<RecurringExpenseService>();
        var schedule = await recurring.AddAsync(vehicle.Id,
            new RecurringExpenseInput("Oil change", null, null, RecurrenceKind.Time, 12, null, Day.AddYears(-1), null, null, null), default);
        await recurring.UpdateAsync(schedule.Item.Id,
            new RecurringExpenseInput("Oil and filter", null, null, RecurrenceKind.Time, 12, null, Day.AddYears(-1), null, null, null), default);
        await recurring.MarkDoneAsync([schedule.Item.Id], new MarkDoneInput(Day.AddDays(5), 10_300, 90, "EUR"), default); // the visit's expense with its link

        await services.GetRequiredService<TyreService>().AddSetAsync(
            new NewTyreSet(vehicle.Id, new TyreSetInput("Winter", TyreSeason.Winter, null, Day, null, null), true, Day, 10_000, true), default);
        return vehicle.Id;
    }

    private sealed record Counts(int Vehicles, int Refuelings, int Expenses, int Schedules, int Completions, int TyreSets, int Readings);

    private static async Task<Counts> CountAsync(TestDatabase database)
    {
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return new Counts(await db.Vehicles.CountAsync(), await db.Refuelings.CountAsync(), await db.Expenses.CountAsync(), await db.RecurringExpenses.CountAsync(),
            await db.RecurringCompletions.CountAsync(), await db.TyreSets.CountAsync(), await db.OdometerReadings.CountAsync());
    }

    private static Counts Cars(int cars, bool logs) =>
        logs ? new Counts(cars, 2 * cars, 2 * cars, cars, cars, cars, 4 * cars) /* readings: two fill-ups, the visit, the swap */ : new Counts(cars, 0, 0, 0, 0, 0, 0);

    [Theory, MemberData(nameof(Providers))]
    public async Task ARunThatReturns_KeepsEverythingItsServicesSaved_AsItsStarter(string provider)
    {
        if (Database(provider) is not { } database) return;
        var logs = provider != "MySql";

        await RunAsync(database, async (_, services) =>
        {
            await AddCarAsync(services, "First", logs);
            await AddCarAsync(services, "Second", logs);
        });

        Assert.Equal(Cars(2, logs), await CountAsync(database));
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.All(await db.Vehicles.ToListAsync(), v =>
        {
            Assert.Equal(Starter.Id, v.OwnerId);
            Assert.NotEqual(default, v.UpdatedAt); // stamped by the sync interceptor, as outside a run
        });
        if (logs) Assert.All(await db.Refuelings.ToListAsync(), r => Assert.Equal(Starter.Id, r.CreatedById));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ARunWhoseWorkThrows_LeavesNothing_AndTheNextContextIsClean(string provider)
    {
        if (Database(provider) is not { } database) return;
        var logs = provider != "MySql";

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, async (_, services) =>
        {
            await AddCarAsync(services, "First", logs);
            throw new InvalidOperationException("unexpected");
        }));

        Assert.Equal("unexpected", thrown.Message);
        Assert.Equal(Cars(0, logs), await CountAsync(database));
        Assert.Null(RunContext.Current);
        // Outside the run again: a save of its own commits by itself.
        await using (var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            Assert.Null(db.Database.CurrentTransaction);
            db.Vehicles.Add(Vehicle.Create(Starter.Id, "Later", null, FuelType.Diesel, MeasurementUnits.Metric));
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, (await CountAsync(database)).Vehicles);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarRolledBackToItsSavepoint_LeavesTheOthers_AndTheRunGoesOn(string provider)
    {
        if (Database(provider) is not { } database) return;
        var logs = provider != "MySql";

        await RunAsync(database, async (run, services) =>
        {
            await run.InSavepointAsync(() => AddCarAsync(services, "First", logs));
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.InSavepointAsync(async () =>
            {
                await AddCarAsync(services, "Left out", logs);
                await run.InSavepointAsync(() => AddCarAsync(services, "Inside it", logs)); // an entry's savepoint inside the car's
                throw new InvalidOperationException("refused");
            }));
            await run.InSavepointAsync(() => AddCarAsync(services, "Third", logs));
        });

        Assert.Equal(Cars(2, logs), await CountAsync(database));
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(["First", "Third"], await db.Vehicles.OrderBy(v => v.Name).Select(v => v.Name).ToListAsync());
    }

    /// <summary>
    /// An add with a client id that is there already ends in a key violation the repository catches (<c>AddOnceAsync</c>). PostgreSQL
    /// refuses every statement after an error in a transaction unless it was rolled back to a savepoint.
    /// </summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task AKeyViolationARepositoryCatches_DoesNotEndTheRun(string provider)
    {
        if (Database(provider) is not { } database) return;
        var logs = provider != "MySql";
        var id = Guid.NewGuid();

        await RunAsync(database, async (_, services) =>
        {
            await AddCarAsync(services, "First", logs: false, id);
            var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            Assert.False(await factory.AddOnceAsync(Vehicle.Create(Starter.Id, "Again", null, FuelType.Petrol, MeasurementUnits.Metric, id), id, default));
            await AddCarAsync(services, "Second", logs);
        });

        var counts = await CountAsync(database);
        Assert.Equal(2, counts.Vehicles);
        Assert.Equal(Cars(1, logs).Refuelings, counts.Refuelings);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task WhatIsOutsideTheDatabase_IsUndoneWithItsSavepointOrRun_AndTheRestWaitsForTheCommit(string provider)
    {
        if (Database(provider) is not { } database) return;
        var done = new List<string>();
        Task Note(string what) { done.Add(what); return Task.CompletedTask; }

        await RunAsync(database, async (run, _) =>
        {
            run.OnRollback(() => Note("undo: the run's"));
            run.AfterCommit(() => Note("after: the run's"));
            await run.InSavepointAsync(() =>
            {
                run.OnRollback(() => Note("undo: kept car"));
                run.AfterCommit(() => Note("after: kept car"));
                return Task.CompletedTask;
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.InSavepointAsync(() =>
            {
                run.OnRollback(() => Note("undo: left out, first"));
                run.OnRollback(() => Note("undo: left out, second"));
                run.AfterCommit(() => Note("after: left out"));
                throw new InvalidOperationException("refused");
            }));
            Assert.Equal(["undo: left out, second", "undo: left out, first"], done); // at once, the newest first
        });
        Assert.Equal(["undo: left out, second", "undo: left out, first", "after: the run's", "after: kept car"], done);

        done.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, async (run, _) =>
        {
            run.AfterCommit(() => Note("after: never"));
            await run.InSavepointAsync(() => { run.OnRollback(() => Note("undo: a car that was kept")); return Task.CompletedTask; });
            run.OnRollback(() => Note("undo: the run's"));
            throw new InvalidOperationException("unexpected");
        }));
        Assert.Equal(["undo: the run's", "undo: a car that was kept"], done);
    }

    /// <summary>A hook that throws is said in the log, and keeps neither the hooks after it from running nor the run's own failure from showing.</summary>
    [Fact]
    public async Task AHookThatThrows_IsLogged_AndTheOthersStillRun()
    {
        var database = Database("Sqlite")!;
        var done = new List<string>();
        Task Note(string what) { done.Add(what); return Task.CompletedTask; }
        var committed = Guid.Empty;

        await RunAsync(database, (run, _) =>
        {
            committed = run.Id;
            run.AfterCommit(() => throw new IOException("in use"));
            run.AfterCommit(() => Note("after: the next one"));
            return Task.CompletedTask;
        });
        var unexpected = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, (run, _) =>
        {
            run.OnRollback(() => Note("undo: the older one"));
            run.OnRollback(() => throw new IOException("in use"));
            throw new InvalidOperationException("unexpected");
        }));

        Assert.Equal("unexpected", unexpected.Message);
        Assert.Equal(["after: the next one", "undo: the older one"], done);
        var warnings = database.Log.From<RunScope>().Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Equal(committed, warnings[0].Values["RunId"]);
        Assert.All(warnings, warning => Assert.IsType<IOException>(warning.Exception));
    }


    [Theory, MemberData(nameof(Providers))]
    public async Task ARunsMark_IsThereWhileItRuns_AndSaysHowItEnded_AlsoWhenItRolledBack(string provider)
    {
        if (Database(provider) is not { } database) return;
        var car = Guid.NewGuid();

        await RunAsync(database, async (run, _) =>
        {
            // Read on another connection: the mark was saved before the run's transaction and is not part of it.
            var open = await ElsewhereAsync(database, _ => OnlyMarkerAsync(database));
            Assert.Equal(run.Id, open.Id);
            Assert.True(open.IsOpen);
            Assert.Equal(Starter.Id, open.StartedById);
            Assert.Equal([car], open.Vehicles.Select(v => v.VehicleId));
        }, car);
        var done = await OnlyMarkerAsync(database);
        Assert.Equal(RunEnd.Done, done.End);
        Assert.NotNull(done.EndedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, (_, _) => throw new InvalidOperationException("unexpected"), car));
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal([RunEnd.Done, RunEnd.Failed], (await db.RunMarkers.OrderBy(m => m.StartedAt).ToListAsync()).Select(m => m.End!.Value));
    }

    /// <summary>The owner's decision 1: the cars in the run wait, on every database; another car's save goes through where the database lets it.</summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task SomeoneElsesSaveToACarOfTheRun_WaitsAndIsRefusedAsBusy_AndGoesThroughOnceTheRunEnded(string provider)
    {
        if (Database(provider) is not { } database) return;
        _user.Principal = Starter;
        Guid held, other;
        await using (var scope = database.Services.CreateAsyncScope())
        {
            held = await AddCarAsync(scope.ServiceProvider, "Held", logs: false);
            other = await AddCarAsync(scope.ServiceProvider, "Other", logs: false);
        }
        Task<Vehicle> Rename(Guid id, string name) => ElsewhereAsync(database, services =>
            services.GetRequiredService<VehicleService>().UpdateAsync(id, name, null, FuelType.Petrol, null, default));

        await RunAsync(database, async (_, services) =>
        {
            await services.GetRequiredService<VehicleService>().UpdateAsync(held, "Held by the run", null, FuelType.Petrol, null, default); // the run's own save passes
            var waited = System.Diagnostics.Stopwatch.StartNew();
            var busy = await Assert.ThrowsAsync<BusyException>(() => Rename(held, "Someone else's"));
            Assert.Equal("import.busy", busy.Key);
            Assert.InRange(waited.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(8)); // it waited the configured second first
            if (provider != "Sqlite") Assert.Equal("Other, renamed", (await Rename(other, "Other, renamed")).Name); // SQLite locks the whole database
        }, held);

        Assert.Equal("Afterwards", (await Rename(held, "Afterwards")).Name);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task AMarkLeftOpenByAServerThatStopped_IsEndedAtStart_AndNoLongerMakesSavesWait(string provider)
    {
        if (Database(provider) is not { } database) return;
        _user.Principal = Starter;
        Guid car;
        await using (var scope = database.Services.CreateAsyncScope()) car = await AddCarAsync(scope.ServiceProvider, "Held", logs: false);
        var markers = database.Get<IRunMarkerRepository>();
        var clock = database.Get<TimeProvider>();
        // On a database server only a mark older than the limit is known to be dead: another server may be in the middle of its run.
        var age = provider == "Sqlite" ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(11);
        var left = RunMarker.Start(Starter.Id, [car], clock.GetUtcNow() - age);
        await markers.AddAsync(left, default);
        var ended = RunMarker.Start(Starter.Id, [car], clock.GetUtcNow() - TimeSpan.FromDays(40));
        await markers.AddAsync(ended, default);
        await markers.EndAsync(ended.Id, RunEnd.Done, clock.GetUtcNow() - TimeSpan.FromDays(40), default);

        var sweep = database.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RunMarkerSweep>().Single();
        await sweep.SweepAsync(atStart: true, default);

        var found = await markers.FindAsync(left.Id, default);
        Assert.Equal(RunEnd.Interrupted, found!.End);
        Assert.Null(await markers.FindAsync(ended.Id, default)); // an ended mark is kept a month
        Assert.Contains(database.Log.Entries, e => e.Message.Contains(left.Id.ToString()) && e.Message.Contains("did not finish"));
        await using var after = database.Services.CreateAsyncScope();
        await after.ServiceProvider.GetRequiredService<VehicleService>().UpdateAsync(car, "Free again", null, FuelType.Petrol, null, default);
    }

    /// <summary>The owner's decision 3: the starter and any administrator can cancel; the run sees it between its entries and leaves nothing.</summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ACancelledRun_LeavesNothing_AndItsMarkSaysCancelled(string provider)
    {
        if (Database(provider) is not { } database) return;
        var logs = provider != "MySql";
        var scope = database.Get<IRunScope>();
        var someoneElse = new Principal(Guid.NewGuid(), "Someone", "someone@example.com", false);
        var administrator = new Principal(Guid.NewGuid(), "Admin", "admin@example.com", true);

        var stopped = await Assert.ThrowsAsync<RunStoppedException>(() => RunAsync(database, async (run, services) =>
        {
            await AddCarAsync(services, "First", logs);
            await run.ThrowIfStoppedAsync(); // not asked yet: goes on
            await Assert.ThrowsAsync<ForbiddenException>(() => scope.CancelAsync(run.Id, someoneElse, default));
            Assert.True(await scope.CancelAsync(run.Id, administrator, default));
            await run.ThrowIfStoppedAsync();
        }));

        Assert.Equal(RunEnd.Cancelled, stopped.Reason);
        Assert.Equal(Cars(0, logs), await CountAsync(database));
        var mark = await OnlyMarkerAsync(database);
        Assert.Equal(RunEnd.Cancelled, mark.End);
        Assert.False(await scope.CancelAsync(mark.Id, Starter, default)); // over already
        Assert.False(await scope.CancelAsync(Guid.NewGuid(), Starter, default));
    }

    /// <summary>A cancel asked for between a run's mark and its transaction (the pause, a connection waited for) is kept for the run.</summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ACancelAskedBeforeTheTransactionOpens_StillStopsTheRun(string provider)
    {
        if (Database(provider, settle: TimeSpan.FromMilliseconds(500)) is not { } database) return;
        var scope = database.Get<IRunScope>();
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = RunAsync(database, async (run, services) =>
        {
            await asked.Task;
            await run.ThrowIfStoppedAsync();
            await AddCarAsync(services, "First", logs: false);
        });
        // The mark is there from the start, the transaction only after the pause: SQLite's run cannot be told through its mark.
        RunMarker? mark = null;
        while (mark is null && !running.IsCompleted) // a run that ended before its mark showed has failed: said below, never waited for
        {
            await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            mark = await db.RunMarkers.AsNoTracking().FirstOrDefaultAsync();
            if (mark is null) await Task.Delay(20);
        }
        Assert.NotNull(mark);
        Assert.True(await scope.CancelAsync(mark.Id, Starter, default));
        asked.SetResult();

        Assert.Equal(RunEnd.Cancelled, (await Assert.ThrowsAsync<RunStoppedException>(() => running)).Reason);
        Assert.Equal(0, (await CountAsync(database)).Vehicles);
        Assert.Equal(RunEnd.Cancelled, (await OnlyMarkerAsync(database)).End);
    }

    /// <summary>A run of another server is asked through its mark; SQLite cannot write a mark while a run is open, and has one server.</summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ARunAskedToCancelThroughItsMark_AsFromAnotherServer_Stops(string provider)
    {
        if (provider == "Sqlite" || Database(provider) is not { } database) return;

        var stopped = await Assert.ThrowsAsync<RunStoppedException>(() => RunAsync(database, async (run, services) =>
        {
            await AddCarAsync(services, "First", logs: false);
            Assert.True(await ElsewhereAsync(database, _ => database.Get<IRunMarkerRepository>().RequestCancelAsync(run.Id, Starter.Id, DateTimeOffset.UtcNow, default)));
            await run.ThrowIfStoppedAsync();
        }));

        Assert.Equal(RunEnd.Cancelled, stopped.Reason);
        Assert.Equal(0, (await CountAsync(database)).Vehicles);
        var mark = await OnlyMarkerAsync(database);
        Assert.Equal((RunEnd.Cancelled, Starter.Id), (mark.End!.Value, mark.CancelRequestedById!.Value));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ARunPastItsLimit_LeavesNothing_AndItsMarkSaysTimedOut(string provider)
    {
        if (Database(provider) is not { } database) return;

        var stopped = await Assert.ThrowsAsync<RunStoppedException>(() => RunAsync(database, async (run, services) =>
        {
            await AddCarAsync(services, "First", logs: false);
            _clock.Ahead = TimeSpan.FromMinutes(9);
            await run.ThrowIfStoppedAsync(); // within the ten minutes
            _clock.Ahead = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);
            await run.ThrowIfStoppedAsync();
        }));

        Assert.Equal(RunEnd.TimedOut, stopped.Reason);
        Assert.Equal(0, (await CountAsync(database)).Vehicles);
        Assert.Equal(RunEnd.TimedOut, (await OnlyMarkerAsync(database)).End);
    }

    /// <summary>
    /// A run alive past its limit (its last entry crossed it) is not taken for a dead one by this server's sweep, and what another
    /// server's sweep guessed is replaced by the run's own word.
    /// </summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ARunStillAlivePastItsLimit_KeepsItsMarkThroughTheSweep_AndItsOwnEndReplacesAnotherServersGuess(string provider)
    {
        if (Database(provider) is not { } database) return;
        var sweep = database.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RunMarkerSweep>().Single();
        var markers = database.Get<IRunMarkerRepository>();

        await RunAsync(database, async (run, services) =>
        {
            await AddCarAsync(services, "First", logs: false);
            _clock.Ahead = TimeSpan.FromMinutes(11);
            if (provider != "Sqlite") // SQLite cannot write a mark while the run is open; its sweep only runs at start
            {
                await ElsewhereAsync(database, async _ => { await sweep.SweepAsync(atStart: false, default); return 0; });
                Assert.True((await markers.FindAsync(run.Id, default))!.IsOpen);
                await ElsewhereAsync(database, async _ => { await markers.EndAsync(run.Id, RunEnd.Interrupted, DateTimeOffset.UtcNow, default); return 0; }); // another server's sweep
            }
        });

        Assert.Equal(RunEnd.Done, (await OnlyMarkerAsync(database)).End);
        Assert.Equal(1, (await CountAsync(database)).Vehicles);
    }

    /// <summary>
    /// The limit is asked between a run's entries, so its last entry may cross it: while the run's transaction is open on this server its
    /// cars are held, whatever the clock says, or someone else's save would cross the run's (the owner's decision 1).
    /// </summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ARunStillAlivePastItsLimit_StillHoldsItsCars(string provider)
    {
        if (Database(provider) is not { } database) return;
        _user.Principal = Starter;
        Guid held;
        await using (var scope = database.Services.CreateAsyncScope()) held = await AddCarAsync(scope.ServiceProvider, "Held", logs: false);

        await RunAsync(database, async (_, services) =>
        {
            await services.GetRequiredService<VehicleService>().UpdateAsync(held, "Held by the run", null, FuelType.Petrol, null, default);
            _clock.Ahead = TimeSpan.FromMinutes(11); // its entry is still being saved, past the ten minutes
            await Assert.ThrowsAsync<BusyException>(() => ElsewhereAsync(database, others =>
                others.GetRequiredService<VehicleService>().UpdateAsync(held, "Someone else's", null, FuelType.Petrol, null, default)));
        }, held);
    }

    /// <summary>What a run removes leaves its tombstone for the devices, committed with the run and gone with it.</summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task WhatARunRemoves_LeavesItsTombstone_OnlyWhenTheRunCommits(string provider)
    {
        if (provider == "MySql" || Database(provider) is not { } database) return; // #345: no schedule can be read on MySQL
        async Task RemoveAScheduleAsync(IServiceProvider services)
        {
            var car = await AddCarAsync(services, "First", logs: false);
            var recurring = services.GetRequiredService<RecurringExpenseService>();
            var schedule = await recurring.AddAsync(car, new RecurringExpenseInput("Oil change", null, null, RecurrenceKind.Time, 12, null, Day, null, null, null), default);
            await recurring.DeleteAsync(schedule.Item.Id, default);
        }
        async Task<int> TombstonesAsync()
        {
            await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await db.Tombstones.CountAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, async (_, services) =>
        {
            await RemoveAScheduleAsync(services);
            throw new InvalidOperationException("unexpected");
        }));
        Assert.Equal(0, await TombstonesAsync());

        await RunAsync(database, (_, services) => RemoveAScheduleAsync(services));
        Assert.Equal(1, await TombstonesAsync());
    }

    /// <summary>
    /// Why reads go on while a run holds SQLite's lock to write, however much it has written: the app's databases are in WAL mode (EF
    /// sets it when it creates the file), where a writer never shuts readers out.
    /// </summary>
    [Fact]
    public async Task TheAppsSqliteDatabase_IsInWalMode_SoARunNeverShutsReadersOut()
    {
        var database = Database("Sqlite")!;
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal("wal", await db.Database.SqlQueryRaw<string>("PRAGMA journal_mode").ToListAsync().ContinueWith(t => t.Result.Single()));
    }

    /// <summary>The logging canary for a run: its lines name the run and its starter by id, never a car or what was saved.</summary>
    [Fact]
    public async Task ARunsLogLines_CarryIdsOnly()
    {
        var database = Database("Sqlite")!;
        Guid id = default;
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, async (run, services) =>
        {
            id = run.Id;
            await AddCarAsync(services, "Canary-Car-7731", logs: false);
            await database.Get<IRunScope>().CancelAsync(run.Id, Starter, default);
            throw new InvalidOperationException("Canary-Failure-7731");
        }));

        var lines = database.Log.From<RunScope>().ToList();
        Assert.Contains(lines, e => e.Message.Contains(id.ToString()) && e.Message.Contains(Starter.Id.ToString()) && e.Message.Contains("started"));
        Assert.Contains(lines, e => e.Message.Contains(id.ToString()) && e.Message.Contains("ended: Failed"));
        Assert.Contains(lines, e => e.Message.Contains("asked to cancel"));
        Assert.False(database.Log.Mentions("Canary-Car-7731"));
        Assert.False(database.Log.Mentions("Canary-Failure-7731")); // the failure is its starter's to log, with what it knows
        Assert.False(database.Log.Mentions(Starter.Email));
    }

    [Fact]
    public async Task ARunInsideARun_IsRefused_AndOutsideARunNobodyIsSignedIn()
    {
        var database = Database("Sqlite")!;
        await RunAsync(database, async (_, _) =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(database, (_, _) => Task.CompletedTask)));

        await using var scope = database.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            scope.ServiceProvider.GetRequiredService<VehicleService>().AddAsync("No one's", null, FuelType.Petrol, MeasurementUnits.Metric, default));
    }
}
