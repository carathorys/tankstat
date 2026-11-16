using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The first step of importing cars from a backup (#336) over the wire: the upload's checks before its body, the file's session asked
/// through GraphQL, the passphrase's tries, the switch, and what the log never holds.
/// </summary>
public sealed class BackupImportApiTests : IDisposable
{
    private const string Status = """
        query($t: String!) { backupImport(token: $t) { token state triesLeft wrongKey refusal { key args }
          file { createdAt version isExport trashedCars cars { id name licensePlate ownerName refuelings expenses schedules fuelType distanceUnit } } } }
        """;
    private const string Open = "mutation($i: OpenBackupImportInput!) { openBackupImport(input: $i) { state triesLeft wrongKey } }";
    private const string Passphrase = "zzcanary correct horse battery";

    private readonly List<TestApp> _apps = [];

    public void Dispose()
    {
        foreach (var app in _apps) app.Dispose();
    }

    private TestApp App(Dictionary<string, string?>? extra = null)
    {
        var app = TestApp.Standalone(extra);
        _apps.Add(app);
        return app;
    }

    /// <summary>A backup of the app's own data, written by the app: Alice's car with a fill-up.</summary>
    private static async Task<(byte[] File, string Car)> BackupAsync(TestApp app, HttpClient alice)
    {
        var car = (await alice.Gql("mutation { addVehicle(input: { name: \"Zzcanary Octavia\", licensePlate: \"ZZ-1\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        (await alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data();
        var backup = await app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return (await File.ReadAllBytesAsync(backup.Path), car);
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient c, byte[] file) =>
        await c.PostAsync("/imports/tankstat", new ByteArrayContent(file) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } });

    private static async Task<string> Token(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<string?> RefusalKey(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();

    private static async Task<JsonElement> Settled(HttpClient c, string token)
    {
        for (var i = 0; i < 600; i++)
        {
            var status = (await c.Gql(Status, new { t = token })).Data().GetProperty("backupImport");
            if (status.GetProperty("state").GetString() != "OPENING") return status;
            await Task.Delay(50);
        }
        throw new TimeoutException("The file was not opened.");
    }

    private string[] Folders(TestApp app)
    {
        var root = Path.Combine(app.ImportScratchPath, "tankstat-import");
        return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
    }

    [Fact]
    public async Task AnUploadedBackup_IsOpened_AndSaysItsCars_ToWhoeverUploadedItOnly()
    {
        var app = App();
        var people = await app.Users();
        var (file, car) = await BackupAsync(app, people.Alice);

        var token = await Token(await Upload(people.Bob, file)); // anyone may read a file they hold: Bob imports Alice's car
        var opened = await Settled(people.Bob, token);

        Assert.Equal(("OPENED", JsonValueKind.Null, JsonValueKind.Null), (opened.GetProperty("state").GetString(), opened.GetProperty("triesLeft").ValueKind, opened.GetProperty("refusal").ValueKind));
        var found = Assert.Single(opened.GetProperty("file").GetProperty("cars").EnumerateArray());
        Assert.Equal((car, "Zzcanary Octavia", "ZZ-1", "alice", 1, 0, 0, "PETROL", "KILOMETERS"),
            (found.GetProperty("id").GetString(), found.GetProperty("name").GetString(), found.GetProperty("licensePlate").GetString(), found.GetProperty("ownerName").GetString(),
                found.GetProperty("refuelings").GetInt32(), found.GetProperty("expenses").GetInt32(), found.GetProperty("schedules").GetInt32(),
                found.GetProperty("fuelType").GetString(), found.GetProperty("distanceUnit").GetString()));
        Assert.False(opened.GetProperty("file").GetProperty("isExport").GetBoolean());

        Assert.Equal("import.expired", (await people.Alice.Gql(Status, new { t = token })).ErrorKey()); // not hers to ask about
        (await people.Bob.Gql("mutation($t: String!) { endBackupImport(token: $t) }", new { t = token })).Data();
        Assert.Equal("import.expired", (await people.Bob.Gql(Status, new { t = token })).ErrorKey());
        Assert.Empty(Folders(app));
    }

    [Fact]
    public async Task TheUpload_ChecksWhoAsks_TheSwitch_AndTheSize_BeforeItsBody()
    {
        var app = App(new() { ["Imports:MaxUploadMb"] = "1" });
        var people = await app.Users();
        var (file, _) = await BackupAsync(app, people.Alice);

        // Nobody signed in: refused, whatever the body.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(app.NewClient(), file)).StatusCode);

        // More than the limit, announced: refused before a byte is read (the body here is never sent in full by a real client).
        var large = await Upload(people.Alice, new byte[1024 * 1024 + 1]);
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "import.tooLarge"), (large.StatusCode, await RefusalKey(large)));

        // The same without an announced length: counted while it is read, and nothing of it stays.
        var chunked = new HttpRequestMessage(HttpMethod.Post, "/imports/tankstat") { Content = new StreamContent(new MemoryStream(new byte[1024 * 1024 + 1])) };
        chunked.Headers.TransferEncodingChunked = true;
        var counted = await people.Alice.SendAsync(chunked);
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "import.tooLarge"), (counted.StatusCode, await RefusalKey(counted)));

        var nothing = await Upload(people.Alice, Encoding.ASCII.GetBytes("not a zip"));
        Assert.Equal((HttpStatusCode.BadRequest, "import.notABackup"), (nothing.StatusCode, await RefusalKey(nothing)));
        Assert.Empty(Folders(app));
    }

    [Fact]
    public async Task WithTheSwitchOff_OnlyAdministratorsImportFromABackup()
    {
        var app = App(new() { ["Imports:BackupEnabled"] = "false" });
        var people = await app.Users();
        var (file, _) = await BackupAsync(app, people.Alice);

        var refused = await Upload(people.Alice, file);
        Assert.Equal((HttpStatusCode.Forbidden, "import.backupDisabled"), (refused.StatusCode, await RefusalKey(refused)));
        Assert.False((await people.Alice.Gql("query { canImportFromBackup }")).Data().GetProperty("canImportFromBackup").GetBoolean());
        Assert.Empty(Folders(app));

        Assert.True((await people.Admin.Gql("query { canImportFromBackup }")).Data().GetProperty("canImportFromBackup").GetBoolean());
        Assert.Equal("OPENED", (await Settled(people.Admin, await Token(await Upload(people.Admin, file)))).GetProperty("state").GetString());
    }

    [Fact]
    public async Task AnEncryptedBackup_TakesItsPassphraseInThreeTries_AndNothingOfTheFileOrThePassphraseIsLogged()
    {
        var salt = Encoding.ASCII.GetBytes("tankstat-salt-01");
        var identity = Tankstat.Infrastructure.Backups.BackupKeys.Identity(Passphrase, salt, Argon2idParameters.Default);
        var app = App(new()
        {
            ["Backups:Encryption:Recipient"] = Tankstat.Domain.Backups.AgeKeys.FormatRecipient(Tankstat.Infrastructure.Backups.BackupKeys.Recipient(identity)),
            ["Backups:Encryption:Salt"] = Convert.ToBase64String(salt),
        });
        var people = await app.Users();
        var (file, _) = await BackupAsync(app, people.Alice);
        // The file's own words, which are the uploader's: a release name that is none.
        file = WithManifest(file, m => m["appVersion"] = "0.21.0-zzcanaryversion");

        var token = await Token(await Upload(people.Alice, file));
        var waiting = (await people.Alice.Gql(Status, new { t = token })).Data().GetProperty("backupImport");
        Assert.Equal(("NEEDS_KEY", 3, false), (waiting.GetProperty("state").GetString(), waiting.GetProperty("triesLeft").GetInt32(), waiting.GetProperty("wrongKey").GetBoolean()));

        (await people.Alice.Gql(Open, new { i = new { token, passphrase = "zzcanary wrong passphrase" } })).Data();
        var wrong = await Settled(people.Alice, token);
        Assert.Equal(("NEEDS_KEY", 2, true), (wrong.GetProperty("state").GetString(), wrong.GetProperty("triesLeft").GetInt32(), wrong.GetProperty("wrongKey").GetBoolean()));

        (await people.Alice.Gql(Open, new { i = new { token, passphrase = Passphrase } })).Data();
        var opened = await Settled(people.Alice, token);
        Assert.Equal(("OPENED", "0.21.0-zzcanaryversion"), (opened.GetProperty("state").GetString(), opened.GetProperty("file").GetProperty("version").GetString()));

        // The log: who and which session, never the passphrases, the file's word about its release, a car's name or a table's.
        Assert.Contains(app.Log.Entries, e => e.Message.Contains(token) && e.Message.Contains(people.AliceId));
        foreach (var never in new[] { "zzcanary", "Zzcanary", "battery", "0.21.0-zzcanaryversion" }) Assert.False(app.Log.Mentions(never), never);
    }

    private static byte[] WithManifest(byte[] backup, Action<JsonObject> change)
    {
        using var source = new ZipArchive(new MemoryStream(backup), ZipArchiveMode.Read);
        using var target = new MemoryStream();
        using (var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in source.Entries)
            {
                using var from = entry.Open();
                using var to = zip.CreateEntry(entry.FullName).Open();
                if (entry.FullName != "manifest.json") { from.CopyTo(to); continue; }
                var json = JsonNode.Parse(new StreamReader(from).ReadToEnd())!.AsObject();
                change(json);
                to.Write(Encoding.UTF8.GetBytes(json.ToJsonString()));
            }
        return target.ToArray();
    }

    [Fact]
    public async Task TheTwoSettings_AreOnTheServerSettingsPage_AndTheFolderIsTheEnvironments()
    {
        var app = App();
        var people = await app.Users();
        var keys = (await people.Admin.Gql("query { systemSettings { groups { settings { key } } } }")).Data().GetProperty("systemSettings").GetProperty("groups")
            .EnumerateArray().SelectMany(g => g.GetProperty("settings").EnumerateArray()).Select(s => s.GetProperty("key").GetString()).ToList();
        Assert.Contains("Imports:BackupEnabled", keys);
        Assert.Contains("Imports:MaxUploadMb", keys);
        Assert.DoesNotContain("Imports:ScratchFolder", keys);
    }
}
