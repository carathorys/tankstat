using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Application.Runs;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// An import from a backup over the wire (#337), from the uploaded file to the cars: the offers, the review, the run asked about until
/// it is done, its report, who is told, and an administrator's cancel. Each test has a host of its own: a run locks SQLite.
/// </summary>
public sealed class BackupImportRunApiTests : IDisposable
{
    private const string Run = """
        id kind startedByName mine state failure ahead fileToken limitMinutes progress { car cars done total } cars { name targetName }
        report { name vehicleId targetName asNew leftOut refuelings expenses schedules existing differing inTrash refusedMore refused { kind date title key args } }
        """;

    private readonly List<TestApp> _apps = [];

    public void Dispose()
    {
        foreach (var app in _apps) app.Dispose();
    }

    private TestApp App(TimeSpan? settle = null)
    {
        var app = TestApp.Standalone(services: s => s.AddSingleton(new RunTimings { Settle = settle ?? TimeSpan.Zero, BusyWait = TimeSpan.FromSeconds(1) }));
        _apps.Add(app);
        return app;
    }

    /// <summary>Alice's Zzcanary Octavia with two fill-ups and a wash, in a backup the app wrote; then the car goes, so the file is all that has it.</summary>
    private static async Task<(byte[] File, string Car)> BackupAsync(TestApp app, HttpClient alice)
    {
        var car = (await alice.Gql("mutation { addVehicle(input: { name: \"Zzcanary Octavia\", licensePlate: \"ZZ-1\", fuelType: DIESEL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        foreach (var (date, odometer) in new[] { ("2026-09-01", 1000), ("2026-09-10", 1600) })
            (await alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
                new { i = new { vehicleId = car, date, volume = 40, totalCost = 60, currency = "EUR", odometer, isFullTank = true } })).Data();
        (await alice.Gql("mutation($i: AddExpenseInput!) { addExpense(input: $i) { id } }", new { i = new { vehicleId = car, date = "2026-09-02", title = "Zzcanary wash", amount = 12, currency = "EUR" } })).Data();
        var backup = await app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return (await File.ReadAllBytesAsync(backup.Path), car);
    }

    private static async Task<string> OpenAsync(HttpClient c, byte[] file)
    {
        var response = await c.PostAsync("/imports/tankstat", new ByteArrayContent(file) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } });
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        for (var i = 0; i < 600; i++)
        {
            if ((await c.Gql("query($t: String!) { backupImport(token: $t) { state } }", new { t = token })).Data().GetProperty("backupImport").GetProperty("state").GetString() != "OPENING") break;
            await Task.Delay(50);
        }
        return token;
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

    /// <summary>A person's notifications once one of the kind is there: a run tells people after its row says how it ended.</summary>
    private static async Task<List<JsonElement>> Told(HttpClient c, string kind)
    {
        for (var i = 0; ; i++)
        {
            var all = await Notifications(c);
            if (i == 100 || all.Any(n => n.GetProperty("kind").GetString() == kind)) return all;
            await Task.Delay(50);
        }
    }

    private static async Task<List<JsonElement>> Notifications(HttpClient c) =>
        [.. (await c.Gql("query { notifications { kind subject { type id } args { name value } } }")).Data().GetProperty("notifications").EnumerateArray()];

    private static string? Arg(JsonElement notification, string name) =>
        notification.GetProperty("args").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).Select(a => a.GetProperty("value").GetString()).FirstOrDefault();

    [Fact]
    public async Task ACarOfAnUploadedBackup_IsReviewed_Imported_Reported_AndThoseItConcernsAreTold()
    {
        var app = App();
        var people = await app.Users();
        var (file, car) = await BackupAsync(app, people.Alice);
        var mine = (await people.Bob.Gql("mutation { addVehicle(input: { name: \"Bob's own\", licensePlate: \"zz 1\", fuelType: DIESEL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var token = await OpenAsync(people.Bob, file);

        var offers = (await people.Bob.Gql("query($t: String!) { backupImportCars(token: $t) { file { version } cars { ownedByMe suggestedTargetId car { id name ownerName refuelings expenses } } targets { id name } } }", new { t = token }))
            .Data().GetProperty("backupImportCars");
        var offer = Assert.Single(offers.GetProperty("cars").EnumerateArray());
        Assert.Equal((car, "alice", false, mine, 2, 1), (offer.GetProperty("car").GetProperty("id").GetString(), offer.GetProperty("car").GetProperty("ownerName").GetString(),
            offer.GetProperty("ownedByMe").GetBoolean(), offer.GetProperty("suggestedTargetId").GetString(), offer.GetProperty("car").GetProperty("refuelings").GetInt32(), offer.GetProperty("car").GetProperty("expenses").GetInt32()));
        Assert.Equal([mine], offers.GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("id").GetString())); // Bob's own cars only

        var review = (await people.Bob.Gql("query($t: String!, $c: [ImportCarChoiceInput!]!) { backupImportReview(token: $t, cars: $c) { name targetName newRefuelings newExpenses existingRefuelings differing inTrash } }",
            new { t = token, c = new[] { new { carId = car } } })).Data().GetProperty("backupImportReview")[0];
        Assert.Equal((2, 1, JsonValueKind.Null), (review.GetProperty("newRefuelings").GetInt32(), review.GetProperty("newExpenses").GetInt32(), review.GetProperty("targetName").ValueKind));

        var started = (await people.Bob.Gql($"mutation($i: StartBackupImportInput!) {{ startBackupImport(input: $i) {{ {Run} }} }}", new { i = new { token, cars = new[] { new { carId = car } } } }))
            .Data().GetProperty("startBackupImport");
        Assert.True(started.GetProperty("mine").GetBoolean());
        var done = await EndedAsync(people.Bob, started.GetProperty("id").GetString()!);

        Assert.Equal(("DONE", JsonValueKind.Null), (done.GetProperty("state").GetString(), done.GetProperty("failure").ValueKind));
        var report = Assert.Single(done.GetProperty("report").EnumerateArray());
        Assert.Equal((true, false, 2, 1, 0), (report.GetProperty("asNew").GetBoolean(), report.GetProperty("leftOut").GetBoolean(), report.GetProperty("refuelings").GetInt32(), report.GetProperty("expenses").GetInt32(), report.GetProperty("refused").GetArrayLength()));
        var vehicles = (await people.Bob.Gql("query { myVehicles { id name refuelingCount } }")).Data().GetProperty("myVehicles").EnumerateArray().ToList();
        Assert.Contains(vehicles, v => v.GetProperty("id").GetString() == report.GetProperty("vehicleId").GetString() && v.GetProperty("name").GetString() == "Zzcanary Octavia" && v.GetProperty("refuelingCount").GetInt32() == 2);

        // Bob sees his run and its report by its id for as long as it is kept; Alice does not, and an administrator only a run that goes or waits.
        Assert.Equal(started.GetProperty("id").GetString(), (await people.Bob.Gql("query { myImportRun { id } }")).Data().GetProperty("myImportRun").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, (await people.Alice.Gql("query($id: UUID!) { importRun(id: $id) { id } }", new { id = started.GetProperty("id").GetString() })).Data().GetProperty("importRun").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await people.Admin.Gql("query($id: UUID!) { importRun(id: $id) { id } }", new { id = started.GetProperty("id").GetString() })).Data().GetProperty("importRun").ValueKind);
        Assert.Equal(0, (await people.Admin.Gql("query { importRuns { id } }")).Data().GetProperty("importRuns").GetArrayLength());
        Assert.Equal("FORBIDDEN", (await people.Bob.Gql("query { importRuns { id } }")).ErrorCode());

        var bobs = Assert.Single(await Told(people.Bob, "IMPORT_FINISHED"), n => n.GetProperty("kind").GetString() == "IMPORT_FINISHED");
        Assert.Equal(("IMPORT_RUN", started.GetProperty("id").GetString(), "1", "Zzcanary Octavia", "0"),
            (bobs.GetProperty("subject").GetProperty("type").GetString(), bobs.GetProperty("subject").GetProperty("id").GetString(), Arg(bobs, "count"), Arg(bobs, "names"), Arg(bobs, "refused")));
        var roots = Assert.Single(await Told(people.Admin, "CARS_IMPORTED"), n => n.GetProperty("kind").GetString() == "CARS_IMPORTED");
        Assert.Equal(("bob", "Zzcanary Octavia"), (Arg(roots, "actor"), Arg(roots, "names")));
        Assert.DoesNotContain(await Notifications(people.Alice), n => n.GetProperty("kind").GetString()!.Contains("IMPORT"));

        // The log: who imported how much, never a car's name or an entry's.
        Assert.Contains(app.Log.Entries, e => e.Message.Contains(people.BobId) && e.Message.Contains("imported 1 cars"));
        Assert.DoesNotContain(app.Log.Entries, e => e.Category.Contains("Import") && (e.Message.Contains("Zzcanary") || e.Message.Contains("wash")));
    }

    [Fact]
    public async Task AnAdministrator_SeesARunThatGoes_AndCancelsIt_NothingComesIn_AndItsStarterIsTold()
    {
        var app = App(settle: TimeSpan.FromSeconds(3)); // long enough to be found running before its transaction opens
        var people = await app.Users();
        var (file, car) = await BackupAsync(app, people.Alice);
        var token = await OpenAsync(people.Bob, file);
        var id = (await people.Bob.Gql("mutation($i: StartBackupImportInput!) { startBackupImport(input: $i) { id } }", new { i = new { token, cars = new[] { new { carId = car } } } }))
            .Data().GetProperty("startBackupImport").GetProperty("id").GetString()!;

        JsonElement listed = default;
        for (var i = 0; i < 100; i++)
        {
            listed = (await people.Admin.Gql($"query {{ importRuns {{ {Run} }} }}")).Data().GetProperty("importRuns");
            if (listed.GetArrayLength() == 1 && listed[0].GetProperty("state").GetString() == "RUNNING") break;
            await Task.Delay(50);
        }
        Assert.Equal(("RUNNING", "bob", false, JsonValueKind.Null), (listed[0].GetProperty("state").GetString(), listed[0].GetProperty("startedByName").GetString(), listed[0].GetProperty("mine").GetBoolean(), listed[0].GetProperty("report").ValueKind));
        Assert.Equal("Zzcanary Octavia", listed[0].GetProperty("cars")[0].GetProperty("name").GetString());
        Assert.False((await people.Alice.Gql("mutation($id: UUID!) { cancelImportRun(id: $id) }", new { id })).Data().GetProperty("cancelImportRun").GetBoolean()); // not hers to cancel

        Assert.True((await people.Admin.Gql("mutation($id: UUID!) { cancelImportRun(id: $id) }", new { id })).Data().GetProperty("cancelImportRun").GetBoolean());
        var ended = await EndedAsync(people.Bob, id);

        Assert.Equal(("NOT_FINISHED", "CANCELLED", JsonValueKind.Null), (ended.GetProperty("state").GetString(), ended.GetProperty("failure").GetString(), ended.GetProperty("report").ValueKind));
        Assert.Equal(token, ended.GetProperty("fileToken").GetString()); // the file is still open: back to the review
        Assert.DoesNotContain((await people.Bob.Gql("query { myVehicles { name } }")).Data().GetProperty("myVehicles").EnumerateArray(), v => v.GetProperty("name").GetString() == "Zzcanary Octavia");
        var told = Assert.Single(await Told(people.Bob, "IMPORT_CANCELLED"), n => n.GetProperty("kind").GetString() == "IMPORT_CANCELLED");
        Assert.StartsWith("Admin", Arg(told, "actor")); // who cancelled it, by name
        Assert.False((await people.Admin.Gql("mutation($id: UUID!) { cancelImportRun(id: $id) }", new { id })).Data().GetProperty("cancelImportRun").GetBoolean()); // over already
    }
}
