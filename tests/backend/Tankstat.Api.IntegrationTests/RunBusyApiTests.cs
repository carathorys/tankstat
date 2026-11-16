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
        var picture = new ByteArrayContent(Png()) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } };
        var upload = await people.Alice.PutAsync($"/media/vehicles/{held}/picture", picture);
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode); // never 503: a client reads that as a server it cannot reach
        Assert.Equal("import.busy", (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());

        (await people.Alice.Gql(Refuel, Fill(other))).Data(); // another car is nobody's business
        (await people.Alice.Gql("query($v: UUID!) { refuelings(vehicleId: $v) { id } }", new { v = held })).Data(); // and reading the held one neither

        await Release(mark);
        (await people.Alice.Gql(Refuel, Fill(held))).Data();
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
