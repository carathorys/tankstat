using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Runs;
using Tankstat.Domain.Runs;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// What someone else's save is told while a run holds its car (#335, the owner's decisions 1 and 2): busy, with a code and a status of
/// its own, on every door; a device's batch stops there and parks nothing. The run itself is not needed for it: its mark is what holds.
/// </summary>
public sealed class RunBusyApiTests : IDisposable
{
    private const string Sync = """
        mutation($i: SyncChangesInput!) { syncChanges(input: $i) { applied parked results { id status } } }
        """;

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1, 2, 3, 4];

    // The wait is the app's ten seconds; here a save is refused at once.
    private readonly TestApp _app = TestApp.Standalone(services: s => s.AddSingleton(new RunTimings { BusyWait = TimeSpan.Zero, Settle = TimeSpan.Zero }));

    public void Dispose() => _app.Dispose();

    private static async Task<string> AddCar(HttpClient c, string name) =>
        (await c.Gql("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name, fuelType = "PETROL" } }))
            .Data().GetProperty("addVehicle").GetProperty("id").GetString()!;

    private async Task<RunMarker> Hold(string car, Guid by)
    {
        var marker = RunMarker.Start(by, [Guid.Parse(car)], DateTimeOffset.UtcNow);
        await _app.Factory.Services.GetRequiredService<IRunMarkerRepository>().AddAsync(marker, default);
        _app.Factory.Services.GetRequiredService<RunGateState>().Forget();
        return marker;
    }

    private async Task Release(RunMarker marker)
    {
        await _app.Factory.Services.GetRequiredService<IRunMarkerRepository>().EndAsync(marker.Id, RunEnd.Done, DateTimeOffset.UtcNow, default);
        _app.Factory.Services.GetRequiredService<RunGateState>().Forget();
    }

    private static object Log(string car, int odometer, Guid? id = null) => new
    {
        id = id ?? Guid.NewGuid(),
        logRefueling = new { id = Guid.NewGuid(), vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer, isFullTank = true },
    };

    [Fact]
    public async Task EveryDoorOfAHeldCar_AnswersBusy_AndAnotherCarIsSaved()
    {
        var people = await _app.Users();
        var held = await AddCar(people.Alice, "Held");
        var other = await AddCar(people.Alice, "Other");
        await people.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = held, userId = people.BobId, level = "EDIT" } });
        var mark = await Hold(held, Guid.Parse(people.AliceId));

        async Task Busy(HttpClient c, string query, object variables)
        {
            var body = await c.Gql(query, variables);
            Assert.Equal(("BUSY", "import.busy"), (body.ErrorCode(), body.ErrorKey()));
        }
        const string Refuel = "mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }";
        object Fill(string car) => new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } };

        await Busy(people.Alice, Refuel, Fill(held)); // the logs' shared edit check
        await Busy(people.Bob, Refuel, Fill(held)); // whoever saves: the run holds the car, not its starter
        await Busy(people.Alice, "mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { id } }", new { i = new { id = held, name = "Renamed", fuelType = "PETROL" } });
        await Busy(people.Alice, "mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = held });
        await Busy(people.Alice, "mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = held, userId = people.BobId, level = "NONE" } });
        // Every service that saves to a car: its logs, schedules and tyres, its charts and dashboard.
        await Busy(people.Alice, "mutation($i: AddExpenseInput!) { addExpense(input: $i) { id } }", new { i = new { vehicleId = held, date = "2026-09-01", title = "Wash", amount = 10, currency = "EUR" } });
        await Busy(people.Alice, "mutation($i: AddRecurringExpenseInput!) { addRecurringExpense(input: $i) { id } }", new { i = new { vehicleId = held, title = "Oil", kind = "TIME", intervalMonths = 12 } });
        await Busy(people.Alice, "mutation($i: AddTyreSetInput!) { addTyreSet(input: $i) { vehicleId } }", new { i = new { vehicleId = held, name = "Winter", season = "WINTER", since = "2026-09-01", fitted = false, day = "2026-09-01" } });
        await Busy(people.Alice, "mutation($i: SaveWidgetInput!) { saveVehicleWidget(input: $i) { id } }",
            new { i = new { vehicleId = held, title = "Bars", shared = true, chart = new { metric = "FUEL_VOLUME", grouping = "MONTH", kind = "BAR", range = "LAST6_MONTHS", stacked = false } } });
        await Busy(people.Alice, "mutation($i: SaveDashboardLayoutInput!) { saveDashboardLayout(input: $i) }", new { i = new { vehicleId = held, personal = false, tiles = Array.Empty<object>(), hidden = Array.Empty<string>(), layoutVersion = 3 } });
        // What is not about one car waits for any run: what a trash holds may be the run's.
        foreach (var empty in new[] { "emptyTrash", "emptyRefuelingTrash", "emptyExpenseTrash", "emptyTyreTrash" })
            await Busy(people.Alice, $"mutation {{ {empty} }}", new { });
        var picture = new ByteArrayContent(Png()) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } };
        var upload = await people.Alice.PutAsync($"/media/vehicles/{held}/picture", picture);
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode); // never 503: a client reads that as a server it cannot reach
        Assert.Equal("import.busy", (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());

        (await people.Alice.Gql(Refuel, Fill(other))).Data(); // another car is nobody's business
        (await people.Alice.Gql("query($v: UUID!) { refuelings(vehicleId: $v) { id } }", new { v = held })).Data(); // and reading the held one neither

        await Release(mark);
        (await people.Alice.Gql(Refuel, Fill(held))).Data();

        // A car in the trash that a run holds: its restore waits too.
        (await people.Alice.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = other })).Data();
        var again = await Hold(other, Guid.Parse(people.AliceId));
        await Busy(people.Alice, "mutation($id: UUID!) { restoreVehicle(id: $id) { id } }", new { id = other });
        await Release(again);
        (await people.Alice.Gql("mutation($id: UUID!) { restoreVehicle(id: $id) { id } }", new { id = other })).Data();
    }

    /// <summary>
    /// SQLite, which these tests run on: an open run holds the database's one lock to write, so a save to any car waits (here a second,
    /// in the app about ten) and is told busy, by the context's save or by a statement of its own; nobody is signed out; reads go on.
    /// </summary>
    [Fact]
    public async Task WhileARunIsOpenOnSqlite_EverySaveIsToldBusy_ASessionRenewalToo_AndReadsGoOn()
    {
        using var app = TestApp.Standalone(services: s => s.AddSingleton(new RunTimings { BusyWait = TimeSpan.FromSeconds(1), Settle = TimeSpan.Zero }));
        var people = await app.Users();
        var other = await AddCar(people.Alice, "Other");
        (await people.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = other, userId = people.BobId, level = "EDIT" } })).Data();
        Assert.Equal(1, (await people.Bob.Gql("query { notificationCount(unreadOnly: true) }")).Data().GetProperty("notificationCount").GetInt32());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starter = new Tankstat.Application.Auth.Principal(Guid.Parse(people.AliceId), "alice", "alice@example.com", false);
        Task run;
        using (ExecutionContext.SuppressFlow())
            run = Task.Run(() => app.Factory.Services.GetRequiredService<IRunScope>().RunAsync(new RunStart(starter, []), async _ =>
            {
                await using var scope = app.Factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Tankstat.Application.Vehicles.VehicleService>().AddAsync(
                    "The run's", null, Tankstat.Domain.Vehicles.FuelType.Petrol, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default); // the lock is taken
                entered.SetResult();
                await release.Task;
            }, default));
        await entered.Task;
        try
        {
            var refused = await people.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
                new { i = new { vehicleId = other, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } });
            Assert.Equal(("BUSY", "import.busy"), (refused.ErrorCode(), refused.ErrorKey()));

            var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/token/refresh") { Headers = { { "X-Requested-With", "fetch" } } };
            var renewed = await people.Alice.SendAsync(refresh);
            Assert.Equal(HttpStatusCode.Conflict, renewed.StatusCode); // not 401: the device stays signed in
            Assert.Equal("import.busy", (await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
            Assert.DoesNotContain(renewed.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.Contains("expires=Thu, 01 Jan 1970"));

            // A statement of its own, not the context's save (marking notifications read is one UPDATE): told busy all the same.
            var marked = await people.Bob.Gql("mutation { markNotificationsRead { id } }"); // Bob's unread one: being given access
            Assert.Equal(("BUSY", "import.busy"), (marked.ErrorCode(), marked.ErrorKey()));

            // The bell's query, which brings the inbox up to date by writing, only reads meanwhile.
            (await people.Alice.Gql("query { notifications(unreadOnly: false) { id } notificationCount(unreadOnly: true) }")).Data();
            (await people.Alice.Gql("query { myVehicles { id } session { user { id } } }")).Data();
        }
        finally
        {
            release.SetResult();
            await run;
        }
        (await people.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = other, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data();
    }

    [Fact]
    public async Task ADevicesBatch_StopsAtTheHeldCar_ParksNothing_AndIsAppliedOnceWhenSentAgain()
    {
        var people = await _app.Users();
        var held = await AddCar(people.Alice, "Held");
        var other = await AddCar(people.Alice, "Other");
        var mark = await Hold(held, Guid.Parse(people.AliceId));
        object[] batch = [Log(other, 1000), Log(held, 1000), Log(other, 1100)];

        var stopped = await people.Alice.Gql(Sync, new { i = new { changes = batch } });

        Assert.Equal(("BUSY", "import.busy"), (stopped.ErrorCode(), stopped.ErrorKey()));
        async Task<int> Count(string car) =>
            (await people.Alice.Gql("query($v: UUID!) { refuelingCount(vehicleId: $v) }", new { v = car })).Data().GetProperty("refuelingCount").GetInt32();
        Assert.Equal((1, 0), (await Count(other), await Count(held))); // what came before it is saved and recorded, the rest waits on the device
        var waiting = (await people.Alice.Gql("query { parkedChanges { id } }")).Data().GetProperty("parkedChanges");
        Assert.Equal(0, waiting.GetArrayLength());

        await Release(mark);
        var again = (await people.Alice.Gql(Sync, new { i = new { changes = batch } })).Data().GetProperty("syncChanges");

        Assert.Equal(0, again.GetProperty("parked").GetInt32());
        Assert.All(again.GetProperty("results").EnumerateArray(), r => Assert.Equal("APPLIED", r.GetProperty("status").GetString()));
        Assert.Equal((2, 1), (await Count(other), await Count(held))); // the first one not a second time
    }
}
