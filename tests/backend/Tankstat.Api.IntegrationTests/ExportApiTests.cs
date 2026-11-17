using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Exports;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// Export my data over the wire (#341): the person's own cars with what each holds, an export started with a passphrase, asked about until
/// it is ready, downloaded by its owner only, replaced, deleted, and the switch that holds users back.
/// </summary>
public sealed class ExportApiTests : IDisposable
{
    private const string Export = "id state carNames encrypted bytes keptUntil failureKey largerThanImport importMaxMb downloadUrl progress { car cars done total }";
    private readonly List<TestApp> _apps = [];

    public void Dispose()
    {
        foreach (var app in _apps) app.Dispose();
    }

    private TestApp App(bool enabled = true)
    {
        var app = TestApp.Standalone(extra: enabled ? null : new Dictionary<string, string?> { ["Exports:Enabled"] = "false" });
        _apps.Add(app);
        return app;
    }

    private static async Task<string> AddCarAsync(HttpClient c, string name)
    {
        var car = (await c.Gql("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name, fuelType = "PETROL" } })).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        (await c.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data();
        return car;
    }

    private static async Task<JsonElement> ReadyAsync(HttpClient c)
    {
        for (var i = 0; i < 600; i++)
        {
            var mine = (await c.Gql($"query {{ myExport {{ {Export} }} }}")).Data().GetProperty("myExport");
            if (mine.GetProperty("state").GetString() != "BUILDING") return mine;
            await Task.Delay(50);
        }
        throw new TimeoutException("The export did not end.");
    }

    [Fact]
    public async Task APerson_ExportsTheirOwnCar_DownloadsIt_ReplacesIt_AndDeletesIt_OverGraphQLAndTheMediaEndpoint()
    {
        var app = App();
        var people = await app.Users();
        var golf = await AddCarAsync(people.Alice, "Zzcanary Golf");
        await AddCarAsync(people.Bob, "Polo");

        Assert.True((await people.Alice.Gql("query { canExport }")).Data().GetProperty("canExport").GetBoolean());
        var offer = Assert.Single((await people.Alice.Gql("query { exportCars { id name refuelings photos photoBytes pictureBytes rowBytes } }")).Data().GetProperty("exportCars").EnumerateArray());
        Assert.Equal((golf, 1), (offer.GetProperty("id").GetString(), offer.GetProperty("refuelings").GetInt32()));
        Assert.Equal(JsonValueKind.Null, (await people.Alice.Gql($"query {{ myExport {{ {Export} }} }}")).Data().GetProperty("myExport").ValueKind);

        // Someone else's car is no car of hers; a passphrase must come twice.
        var polo = (await people.Bob.Gql("query { exportCars { id } }")).Data().GetProperty("exportCars")[0].GetProperty("id").GetString();
        Assert.Equal("vehicle.notFound", (await people.Alice.Gql($"mutation($i: StartExportInput!) {{ startExport(input: $i) {{ id }} }}", new { i = new { cars = new[] { new { vehicleId = polo } } } })).ErrorKey());
        Assert.Equal("password.mismatch", (await people.Alice.Gql("mutation($i: StartExportInput!) { startExport(input: $i) { id } }",
            new { i = new { cars = new[] { new { vehicleId = golf } }, passphrase = "a long enough passphrase", passphraseConfirmation = "another one" } })).ErrorKey());

        var started = (await people.Alice.Gql($"mutation($i: StartExportInput!) {{ startExport(input: $i) {{ {Export} }} }}",
            new { i = new { cars = new[] { new { vehicleId = golf, photos = false } }, passphrase = "a long enough passphrase", passphraseConfirmation = "a long enough passphrase" } }))
            .Data().GetProperty("startExport");
        Assert.Equal("BUILDING", started.GetProperty("state").GetString());
        var ready = await ReadyAsync(people.Alice);
        Assert.Equal(("READY", true, false), (ready.GetProperty("state").GetString(), ready.GetProperty("encrypted").GetBoolean(), ready.GetProperty("largerThanImport").GetBoolean()));
        var url = ready.GetProperty("downloadUrl").GetString()!;

        var download = await people.Alice.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("no-store", download.Headers.CacheControl!.ToString());
        Assert.StartsWith("tankstat-export-", download.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal(ready.GetProperty("bytes").GetInt64(), (await download.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await people.Bob.GetAsync(url)).StatusCode); // anyone else's looks non-existent
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync(url)).StatusCode);

        // A new export replaces it; deleting it leaves nothing.
        (await people.Alice.Gql($"mutation($i: StartExportInput!) {{ startExport(input: $i) {{ id }} }}", new { i = new { cars = new[] { new { vehicleId = golf } } } })).Data();
        var second = await ReadyAsync(people.Alice);
        Assert.NotEqual(ready.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await people.Alice.GetAsync(url)).StatusCode);
        Assert.True((await people.Alice.Gql("mutation { deleteExport }")).Data().GetProperty("deleteExport").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await people.Alice.Gql($"query {{ myExport {{ {Export} }} }}")).Data().GetProperty("myExport").ValueKind);

        // The administrators were told; the log names ids and counts, never the car or the passphrase.
        var told = (await people.Admin.Gql("query { notifications { kind args { name value } } }")).Data().GetProperty("notifications").EnumerateArray();
        Assert.Contains(told, n => n.GetProperty("kind").GetString() == "CARS_EXPORTED");
        Assert.False(app.Log.Mentions("a long enough passphrase"));
        Assert.DoesNotContain(app.Log.Entries.Where(e => e.Category.StartsWith("Tankstat", StringComparison.Ordinal)), e => e.Text.Contains("Zzcanary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SwitchedOff_AUserIsRefused_AndAnAdministratorStillExports()
    {
        var app = App(enabled: false);
        var people = await app.Users();
        await AddCarAsync(people.Alice, "Golf");
        var fiat = await AddCarAsync(people.Admin, "Fiat");

        Assert.False((await people.Alice.Gql("query { canExport }")).Data().GetProperty("canExport").GetBoolean());
        Assert.Equal(ExportService.DisabledKey, (await people.Alice.Gql("query { exportCars { id } }")).ErrorKey());
        Assert.True((await people.Admin.Gql("query { canExport }")).Data().GetProperty("canExport").GetBoolean());
        (await people.Admin.Gql("mutation($i: StartExportInput!) { startExport(input: $i) { id } }", new { i = new { cars = new[] { new { vehicleId = fiat } } } })).Data();
        Assert.Equal("READY", (await ReadyAsync(people.Admin)).GetProperty("state").GetString());
        _ = app.Factory.Services.GetRequiredService<IExportFiles>();
    }
}
