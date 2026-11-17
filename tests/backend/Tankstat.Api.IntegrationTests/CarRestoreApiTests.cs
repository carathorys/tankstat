using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Application.Runs;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// Restore cars over the wire (#340): a restore point opened by an administrator, its cars in their states, the people for the picker,
/// the review, the Choose dialog's page for a car still here, the run asked about until it is done, its report, and who is told. Each test
/// has a host of its own: a run locks SQLite.
/// </summary>
public sealed class CarRestoreApiTests : IDisposable
{
    private const string Run = """
        id kind backupOf state failure fileToken cars { name } report { name vehicleId leftOut refuelings refused { key } restore { ownerId ownerName cameBack grants goneGrantees peopleLeftOut } }
        """;

    private readonly List<TestApp> _apps = [];

    public void Dispose()
    {
        foreach (var app in _apps) app.Dispose();
    }

    private TestApp App()
    {
        var app = TestApp.Standalone(services: s => s.AddSingleton(new RunTimings { Settle = TimeSpan.Zero, BusyWait = TimeSpan.FromSeconds(1) }));
        _apps.Add(app);
        return app;
    }

    private static async Task<string> AddCarAsync(HttpClient c, string name, params (string Date, int Odometer)[] fills)
    {
        var car = (await c.Gql("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name, fuelType = "PETROL" } })).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        foreach (var (date, odometer) in fills)
            (await c.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
                new { i = new { vehicleId = car, date, volume = 40, totalCost = 60, currency = "EUR", odometer, isFullTank = true } })).Data();
        return car;
    }

    private static async Task<JsonElement> OpenedAsync(HttpClient admin, string file)
    {
        var status = (await admin.Gql("mutation($f: String!) { openCarRestore(file: $f) { token state file { createdAt cars { id ownerName } } } }", new { f = file })).Data().GetProperty("openCarRestore");
        for (var i = 0; i < 600 && status.GetProperty("state").GetString() == "OPENING"; i++)
        {
            await Task.Delay(50);
            status = (await admin.Gql("query($t: String!) { carRestore(token: $t) { token state file { createdAt cars { id ownerName } } } }", new { t = status.GetProperty("token").GetString() }))
                .Data().GetProperty("carRestore");
        }
        Assert.Equal("OPENED", status.GetProperty("state").GetString());
        return status;
    }

    private static async Task<JsonElement> EndedAsync(HttpClient c, string id)
    {
        for (var i = 0; i < 600; i++)
        {
            var run = (await c.Gql($"query($id: UUID!) {{ importRun(id: $id) {{ {Run} }} }}", new { id })).Data().GetProperty("importRun");
            if (run.GetProperty("state").GetString() is "DONE" or "NOT_FINISHED") return run;
            await Task.Delay(100);
        }
        throw new TimeoutException("The run did not end.");
    }

    private static async Task<List<JsonElement>> Told(HttpClient c, string kind)
    {
        for (var i = 0; ; i++)
        {
            var all = (await c.Gql("query { notifications { kind args { name value } } }")).Data().GetProperty("notifications").EnumerateArray().ToList();
            if (i == 100 || all.Any(n => n.GetProperty("kind").GetString() == kind)) return all;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task AnAdministrator_RestoresAPurgedCarAndAMissingEntry_FromARestorePoint_OverGraphQL()
    {
        var app = App();
        var people = await app.Users();
        var gone = await AddCarAsync(people.Alice, "Zzcanary Golf", ("2026-09-01", 1000), ("2026-09-10", 1600));
        var here = await AddCarAsync(people.Alice, "Zzcanary Fiat", ("2026-09-01", 5000), ("2026-09-11", 5400));
        var backup = await app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        // Since the backup: the Golf purged, one of the Fiat's fill-ups purged.
        var fiatFills = (await people.Alice.Gql("query($id: UUID!) { refuelings(vehicleId: $id) { id odometer } }", new { id = here })).Data().GetProperty("refuelings").EnumerateArray().ToList();
        var lost = fiatFills.Single(r => r.GetProperty("odometer").GetInt64() == 5400).GetProperty("id").GetString();
        (await people.Alice.Gql("mutation($id: UUID!) { deleteRefueling(id: $id) { id } }", new { id = lost })).Data();
        (await people.Alice.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = gone })).Data();
        (await people.Alice.Gql("mutation { emptyTrash }")).Data();
        (await people.Alice.Gql("mutation { emptyRefuelingTrash }")).Data();

        // Only an administrator opens a restore point.
        Assert.Equal("FORBIDDEN", (await people.Bob.Gql("mutation($f: String!) { openCarRestore(file: $f) { token } }", new { f = Path.GetFileName(backup.Path) })).ErrorCode());
        var opened = await OpenedAsync(people.Admin, Path.GetFileName(backup.Path));
        var token = opened.GetProperty("token").GetString()!;

        var page = (await people.Admin.Gql("query($t: String!) { restoreCars(token: $t, search: \"zzcanary\", skip: 0, take: 10) { total owners { name } cars { state missingRefuelings ownerNameHere car { id name ownerEmail } } } }",
            new { t = token })).Data().GetProperty("restoreCars");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        var states = page.GetProperty("cars").EnumerateArray().ToDictionary(c => c.GetProperty("car").GetProperty("id").GetString()!);
        Assert.Equal(("GONE", "alice@example.com"), (states[gone].GetProperty("state").GetString(), states[gone].GetProperty("car").GetProperty("ownerEmail").GetString()));
        Assert.Equal(("HERE", 1, "alice"), (states[here].GetProperty("state").GetString(), states[here].GetProperty("missingRefuelings").GetInt32(), states[here].GetProperty("ownerNameHere").GetString()));
        Assert.Contains((await people.Admin.Gql("query { restorePeople { name email } }")).Data().GetProperty("restorePeople").EnumerateArray(), p => p.GetProperty("email").GetString() == "bob@example.com");

        var cars = new object[] { new { carId = gone }, new { carId = here } };
        var review = (await people.Admin.Gql("query($t: String!, $c: [ImportCarChoiceInput!]!) { restoreReview(token: $t, cars: $c) { carId newRefuelings restore { state ownerName } } }", new { t = token, c = cars }))
            .Data().GetProperty("restoreReview").EnumerateArray().ToDictionary(r => r.GetProperty("carId").GetString()!);
        Assert.Equal(("GONE", 2), (review[gone].GetProperty("restore").GetProperty("state").GetString(), review[gone].GetProperty("newRefuelings").GetInt32()));
        Assert.Equal(("HERE", "alice", 1), (review[here].GetProperty("restore").GetProperty("state").GetString(), review[here].GetProperty("restore").GetProperty("ownerName").GetString(),
            review[here].GetProperty("newRefuelings").GetInt32()));
        var entries = (await people.Admin.Gql("query($t: String!, $c: UUID!) { restoreEntries(token: $t, carId: $c, kind: \"refueling\", differing: false, skip: 0, take: 10) { total items { id odometer } } }",
            new { t = token, c = here })).Data().GetProperty("restoreEntries");
        Assert.Equal((1, lost), (entries.GetProperty("total").GetInt32(), entries.GetProperty("items")[0].GetProperty("id").GetString()));

        var started = (await people.Admin.Gql($"mutation($i: StartCarRestoreInput!) {{ startCarRestore(input: $i) {{ {Run} }} }}", new { i = new { token, cars } })).Data().GetProperty("startCarRestore");
        Assert.Equal("RESTORE", started.GetProperty("kind").GetString());
        var done = await EndedAsync(people.Admin, started.GetProperty("id").GetString()!);
        Assert.Equal("DONE", done.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("backupOf").ValueKind);
        var golf = done.GetProperty("report").EnumerateArray().Single(c => c.GetProperty("vehicleId").GetString() == gone);
        Assert.Equal((2, true, "alice"), (golf.GetProperty("refuelings").GetInt32(), golf.GetProperty("restore").GetProperty("cameBack").GetBoolean(), golf.GetProperty("restore").GetProperty("ownerName").GetString()));
        Assert.Equal(started.GetProperty("id").GetString(), (await people.Admin.Gql("query { myRestoreRun { id } }")).Data().GetProperty("myRestoreRun").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, (await people.Admin.Gql("query { myImportRun { id } }")).Data().GetProperty("myImportRun").ValueKind); // the Import page never shows it

        // Alice has both cars and the lost fill-up back, under its id, and is told.
        var fiat = (await people.Alice.Gql("query($id: UUID!) { refuelings(vehicleId: $id) { id createdByGone } }", new { id = here })).Data().GetProperty("refuelings").EnumerateArray().ToList();
        Assert.Contains(fiat, r => r.GetProperty("id").GetString() == lost && !r.GetProperty("createdByGone").GetBoolean());
        Assert.Contains((await people.Alice.Gql("query { myVehicles { id } }")).Data().GetProperty("myVehicles").EnumerateArray(), v => v.GetProperty("id").GetString() == gone);
        Assert.Contains(await Told(people.Alice, "CAR_RESTORED"), n => n.GetProperty("kind").GetString() == "CAR_RESTORED");
        Assert.True((await people.Admin.Gql("mutation($t: String!) { endCarRestore(token: $t) }", new { t = token })).Data().GetProperty("endCarRestore").GetBoolean());
    }

    [Fact]
    public async Task ATokenOfAnUpload_IsNoRestoreSource()
    {
        var app = App();
        var people = await app.Users();
        Assert.Equal("restore.expired", (await people.Admin.Gql("query { restoreCars(token: \"nope\", skip: 0, take: 10) { total } }")).ErrorKey());
        Assert.Equal("restore.expired", (await people.Admin.Gql("query { carRestore(token: \"nope\") { state } }")).ErrorKey());
    }
}
