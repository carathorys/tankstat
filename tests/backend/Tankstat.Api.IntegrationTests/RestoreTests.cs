using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// A restore at start: a host whose backups folder holds a backup and a marker starts, replaces its data with the backup's (an empty
/// database too, since the migrator runs first), gets a new generation, writes a safety-net backup first, and refuses what it must.
/// </summary>
public sealed class RestoreTests : IDisposable
{
    private const string Sync = "mutation($i: SyncChangesInput!) { syncChanges(input: $i) { applied parked results { id status entityId version } } }";
    private const string Instance = "{ session { instance { generation restorePoint } } }";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-restore");
    private string Folder(string name) => Path.Combine(_root, name);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TestApp Host(string name, bool migrated, Dictionary<string, string?>? extra = null, string? backups = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "None",
            ["Backups:Path"] = backups ?? Folder(name + "-backups"),
            ["Storage:Path"] = Folder(name + "-uploads"),
            ["Storage:PhotosPath"] = Folder(name + "-photos"),
            ["DataProtection:KeysPath"] = Folder(name + "-keys"),
        };
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        return new TestApp(settings, migrated: migrated);
    }

    private static async Task<string> AddVehicle(TestApp app, string name)
    {
        using var client = app.NewClient();
        return (await client.Gql("mutation($n: String!) { addVehicle(input: { name: $n, fuelType: PETROL }) { id } }", new { n = name })).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
    }

    private static async Task<string[]> Vehicles(TestApp app)
    {
        using var client = app.NewClient();
        return (await client.Gql("{ myVehicles { name } }")).Data().GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).Order().ToArray();
    }

    private static async Task<(string Generation, string? RestorePoint)> InstanceOf(TestApp app)
    {
        using var client = app.NewClient();
        var instance = (await client.Gql(Instance)).Data().GetProperty("session").GetProperty("instance");
        return (instance.GetProperty("generation").GetString()!, instance.GetProperty("restorePoint").GetString());
    }

    private static Task<BackupResult> Backup(TestApp app, BackupRequest? request = null) =>
        app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(request ?? new BackupRequest(), default);

    /// <summary>Puts a backup into a folder and the marker that asks for it, as the mutation (or the CLI) does.</summary>
    private static string Stage(string backupsFolder, string backupFile, string? identity = null)
    {
        Directory.CreateDirectory(backupsFolder);
        var staged = Path.Combine(backupsFolder, Path.GetFileName(backupFile));
        File.Copy(backupFile, staged);
        new RestoreMarker(Path.GetFileName(backupFile), Guid.Empty, DateTimeOffset.UtcNow, identity).Write(backupsFolder);
        return staged;
    }

    [Fact]
    public async Task AHostWithAMarker_StartsWithTheBackupsData_FromAnEmptyDatabase_WritesASafetyNet_AndHasANewGeneration()
    {
        using var a = Host("a", migrated: true);
        await AddVehicle(a, "Golf");
        await AddVehicle(a, "Polo");
        var (generationA, _) = await InstanceOf(a);
        var backup = await Backup(a);

        var folderB = Folder("b-backups");
        Stage(folderB, backup.Path);
        using var b = Host("b", migrated: false, backups: folderB);

        Assert.Equal(["Golf", "Polo"], await Vehicles(b));
        var (generationB, restorePoint) = await InstanceOf(b);
        Assert.NotEqual(generationA, generationB);
        Assert.Equal(backup.Manifest.CreatedAt, DateTimeOffset.Parse(restorePoint!));
        Assert.False(File.Exists(RestoreMarker.PathIn(folderB)));
        var files = Directory.GetFiles(folderB).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(2, files.Count); // the backup, and the safety net
        var safetyNet = files.Single(f => f != Path.GetFileName(backup.Path));
        Assert.True(BackupFile.ReadManifest(Path.Combine(folderB, safetyNet!)).SafetyNet);
        Assert.DoesNotContain(files, f => f!.StartsWith("restore."));
        var lines = b.Log.From("Tankstat.Infrastructure.Backups.RestoreStep").Where(e => e.Level == LogLevel.Information).ToList();
        Assert.Contains(lines, e => e.Values.ContainsKey("Generation") && (Guid)e.Values["Generation"]! == Guid.Parse(generationB));
        Assert.DoesNotContain(b.Log.From("Tankstat"), e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ARestore_ReplacesTheDataAsAWhole_PicturesAndKeysIncluded_AndARestoredSessionStillWorks()
    {
        // A Standalone host: its administrator signs in (a session row, the keys that protect the cookies), uploads a picture.
        var standalone = TestApp.StandaloneSettings();
        using var a = Host("a", migrated: true, extra: standalone);
        var client = a.NewClient();
        await client.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);
        var car = (await client.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var picture = await client.PutAsync($"/media/vehicles/{car}/picture", new ByteArrayContent([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]) { Headers = { ContentType = new("image/jpeg") } });
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        var pictureUrl = (await client.Gql("{ myVehicles { pictureUrl } }")).Data().GetProperty("myVehicles")[0].GetProperty("pictureUrl").GetString()!;
        var cookies = await CookiesOf(a);

        var identity = BackupKeys.Identity("hunter2 hunter2", Encoding.ASCII.GetBytes("tankstat-salt-01"), new Argon2idParameters(256, 1, 1));
        var encryption = new BackupEncryption(BackupEncryption.AgeX25519, AgeKeys.FormatRecipient(BackupKeys.Recipient(identity)), Convert.ToBase64String(Encoding.ASCII.GetBytes("tankstat-salt-01")), new Argon2idParameters(256, 1, 1));
        var backup = await Backup(a, new BackupRequest(Encryption: encryption));
        Assert.True(backup.Manifest.Contents.Keys);

        // B has data of its own, another administrator even, and its own keys: all of it replaced at its restart.
        var folderB = Folder("b-backups");
        using var shared = new SharedData();
        var b = shared.Host(TestApp.StandaloneSettings(new() { ["Auth:Standalone:AdminEmail"] = "other@example.com", ["Backups:Path"] = folderB }));
        var ownClient = b.NewClient();
        await ownClient.LoginAs("other@example.com", TestApp.AdminPassword);
        await ownClient.Gql("mutation { addVehicle(input: { name: \"Own car\", fuelType: PETROL }) { id } }");
        b.Dispose(); // stopped, like a server before its restart

        Stage(folderB, backup.Path, AgeKeys.FormatIdentity(identity));
        using var restarted = shared.Host(TestApp.StandaloneSettings(new() { ["Backups:Path"] = folderB }));

        // A's administrator's cookies work on the restarted B (the restored keys read them, the restored session row is there); B's own are gone.
        var kept = restarted.NewClientHolding(cookies);
        var session = (await kept.Gql("{ session { user { email } } myVehicles { name pictureUrl } }")).Data();
        Assert.Equal(TestApp.AdminEmail, session.GetProperty("session").GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(["Golf"], session.GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(pictureUrl, session.GetProperty("myVehicles")[0].GetProperty("pictureUrl").GetString());
        var image = await kept.GetAsync(pictureUrl);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode); // the picture file came along
        var refresh = new HttpRequestMessage(HttpMethod.Post, "/auth/token/refresh") { Headers = { { "X-Requested-With", "fetch" }, { "Cookie", cookies } } };
        Assert.Equal(HttpStatusCode.NoContent, (await restarted.NewClientWithoutRedirects().SendAsync(refresh)).StatusCode);
        var other = restarted.NewClient();
        Assert.NotNull((await other.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "other@example.com", password = TestApp.AdminPassword } })).ErrorCode()); // B's own administrator is gone
    }

    /// <summary>Both cookies of a signed-in client, as a browser would send them to the token endpoints.</summary>
    private static async Task<string> CookiesOf(TestApp app)
    {
        var response = await app.NewClient().PostAsJsonAsync("/graphql", new
        {
            query = "mutation($i: LoginInput!) { login(input: $i) { id } }",
            variables = new { i = new { email = TestApp.AdminEmail, password = TestApp.AdminPassword } },
        });
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(h => h[..h.IndexOf(';')]));
    }

    [Theory]
    [InlineData("otherMigration")]
    [InlineData("missingFile")]
    [InlineData("wrongIdentity")]
    [InlineData("damaged")]
    public async Task ARestoreThatCannotBeApplied_IsRefused_TheMarkerRenamed_AndTheHostServesItsOwnData(string how)
    {
        using var a = Host("a", migrated: true);
        await AddVehicle(a, "Golf");
        var identity = BackupKeys.Identity("hunter2 hunter2", Encoding.ASCII.GetBytes("tankstat-salt-01"), new Argon2idParameters(256, 1, 1));
        var encryption = new BackupEncryption(BackupEncryption.AgeX25519, AgeKeys.FormatRecipient(BackupKeys.Recipient(identity)), Convert.ToBase64String(Encoding.ASCII.GetBytes("tankstat-salt-01")), new Argon2idParameters(256, 1, 1));
        var backup = await Backup(a, new BackupRequest(Encryption: encryption));

        var folderB = Folder("b-backups");
        var staged = Stage(folderB, backup.Path, how == "wrongIdentity" ? AgeKeys.FormatIdentity(BackupKeys.Identity("nope", Encoding.ASCII.GetBytes("tankstat-salt-01"), new Argon2idParameters(256, 1, 1))) : AgeKeys.FormatIdentity(identity));
        switch (how)
        {
            case "otherMigration":
                RewriteManifest(staged, json => json.Replace(backup.Manifest.Migration!, "20200101000000_SomethingElse"));
                break;
            case "missingFile":
                File.Delete(staged);
                break;
            case "damaged":
                var bytes = await File.ReadAllBytesAsync(staged);
                bytes[300] ^= 0xff;
                await File.WriteAllBytesAsync(staged, bytes);
                break;
        }
        using var b = Host("b", migrated: true, backups: folderB);
        await AddVehicle(b, "Own car"); // starts, with its own data

        Assert.Equal(["Own car"], await Vehicles(b));
        Assert.False(File.Exists(RestoreMarker.PathIn(folderB)));
        Assert.Single(Directory.GetFiles(folderB, "restore.refused.*"));
        Assert.DoesNotContain(Directory.GetFiles(folderB, "tankstat-backup-*"), f => f != staged && BackupFile.ReadManifest(f).SafetyNet); // no safety net: nothing was about to be replaced
        var refusal = Assert.Single(b.Log.From("Tankstat.Infrastructure.Backups.RestoreStep").Where(e => e.Level == LogLevel.Error));
        Assert.Contains(how switch { "otherMigration" => "otherMigration", "wrongIdentity" => "wrongKey", "damaged" => "corrupt", _ => "cannot be read" }, (string)refusal.Values["Reason"]!);
    }

    [Fact]
    public async Task ABackupWithoutThePhotos_RestoredWhereTheyLiveWithThePictures_LeavesThePhotosOnDiskAlone()
    {
        // A keeps its photos apart and backs up without them; B keeps everything in one folder, where the operator put the photos back by hand.
        using var a = Host("a", migrated: true);
        var clientA = a.NewClient();
        var car = await AddVehicle(a, "Golf");
        var log = (await clientA.Gql("mutation($v: UUID!) { logRefueling(input: { vehicleId: $v, date: \"2026-09-01\", volume: 40, totalCost: 60, currency: \"EUR\", odometer: 1000, isFullTank: true }) { id } }", new { v = car }))
            .Data().GetProperty("logRefueling").GetProperty("id").GetString()!;
        var jpeg = () => new ByteArrayContent([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]) { Headers = { ContentType = new("image/jpeg") } };
        Assert.Equal(HttpStatusCode.OK, (await clientA.PutAsync($"/media/vehicles/{car}/picture", jpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientA.PutAsync($"/media/refuelings/{log}/photos", jpeg())).StatusCode);
        var urls = (await clientA.Gql("query($v: UUID!) { myVehicles { pictureUrl } refuelings(vehicleId: $v) { photos { url } } }", new { v = car })).Data();
        var pictureUrl = urls.GetProperty("myVehicles")[0].GetProperty("pictureUrl").GetString()!;
        var photoUrl = urls.GetProperty("refuelings")[0].GetProperty("photos")[0].GetProperty("url").GetString()!;
        var backup = await Backup(a);
        Assert.Equal(BackupPhotos.LeftOut, backup.Manifest.Contents.Photos);

        var folderB = Folder("b-backups");
        Stage(folderB, backup.Path);
        var picturesB = Folder("b-uploads");
        foreach (var file in Directory.EnumerateFiles(Folder("a-photos"), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(picturesB, Path.GetRelativePath(Folder("a-photos"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        using var b = new TestApp(new() { ["Auth:Mode"] = "None", ["Backups:Path"] = folderB, ["Storage:Path"] = picturesB, ["Storage:PhotosPath"] = null, ["DataProtection:KeysPath"] = Folder("b-keys") }, migrated: false);
        var clientB = b.NewClient();

        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync(pictureUrl)).StatusCode); // from the backup
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync(photoUrl)).StatusCode); // not in the backup: the file on disk stayed and matches its log
        Assert.Contains(b.Log.From("Tankstat.Infrastructure.Backups.RestoreStep"), e => e.Values.TryGetValue("Kept", out var kept) && ((string?)kept)?.Length > 0);
    }

    /// <summary>Changes the plain manifest of a backup file (the checksum covers the inner archive only).</summary>
    private static void RewriteManifest(string file, Func<string, string> change)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Update);
        var entry = zip.GetEntry(BackupManifest.FileName)!;
        string json;
        using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(BackupManifest.FileName, CompressionLevel.NoCompression).Open());
        writer.Write(change(json));
    }

    [Fact]
    public async Task AfterARestore_TheSyncLedgerIsTheBackups_SoARepeatedAddIsAnsweredFromIt_AndAChangeFromAfterTheBackupParks()
    {
        using var a = Host("a", migrated: true);
        var carId = Guid.NewGuid();
        var changeId = Guid.NewGuid();
        var clientA = a.NewClient();
        var first = (await clientA.Gql(Sync, new { i = new { changes = new object[] { new { id = changeId, addVehicle = new { id = carId, name = "Golf", fuelType = "PETROL" } } } } })).Data().GetProperty("syncChanges");
        Assert.Equal(1, first.GetProperty("applied").GetInt32());
        var backup = await Backup(a);
        // Edited after the backup: version 2 on A, version 1 in the backup.
        await clientA.Gql("mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { id } }", new { i = new { id = carId, name = "Golf GTI", fuelType = "PETROL" } });

        var folderB = Folder("b-backups");
        Stage(folderB, backup.Path);
        using var b = Host("b", migrated: false, backups: folderB);
        var clientB = b.NewClient();

        var again = (await clientB.Gql(Sync, new { i = new { changes = new object[] { new { id = changeId, addVehicle = new { id = carId, name = "Golf", fuelType = "PETROL" } } } } })).Data().GetProperty("syncChanges");
        Assert.Equal("APPLIED", again.GetProperty("results")[0].GetProperty("status").GetString()); // from the ledger, not applied twice
        Assert.Equal(carId.ToString(), again.GetProperty("results")[0].GetProperty("entityId").GetString());
        Assert.Equal(["Golf"], await Vehicles(b));
        var late = (await clientB.Gql(Sync, new { i = new { changes = new object[] { new { id = Guid.NewGuid(), expectedVersion = 2, updateVehicle = new { id = carId, name = "Polo", fuelType = "PETROL" } } } } })).Data().GetProperty("syncChanges");
        Assert.Equal(1, late.GetProperty("parked").GetInt32()); // the device's version is from the history the restore replaced
    }
}
