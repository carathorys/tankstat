using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The backups on the administration page: the restore points in the folder (a file copied in by hand too), the sizes, a backup written
/// now, an upload streamed into the folder and a download out of it, and a key set for encrypted backups. Administrators only.
/// </summary>
public sealed class BackupAdminTests : IDisposable
{
    private const string Overview = """
        { backups { folder migration restorePoints { file bytes createdAt appVersion provider migration sameMigration olderMigration safetyNet uploaded encrypted recipient currentRecipient photos keys tables rows }
          sizes { database pictures photos } run { running startedAt finishedAt file bytes error } settings { recipient photosApart photos maxUploadMb } } }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-backups");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private TestApp App(Dictionary<string, string?>? extra = null, Action<IServiceCollection>? services = null)
    {
        var settings = new Dictionary<string, string?> { ["Backups:Path"] = _folder };
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        return TestApp.Standalone(settings, services);
    }

    private static async Task<HttpClient> Admin(TestApp app)
    {
        var client = app.NewClient();
        await client.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);
        return client;
    }

    private static async Task<string> WriteBackup(TestApp app) =>
        (await app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default)).Path;

    private static async Task Idle(TestApp app) => await ((BackupRunner)app.Factory.Services.GetRequiredService<IBackupRunner>()).WhenIdleAsync();

    private static Task<HttpResponseMessage> Upload(HttpClient client, byte[] body, string name = "my backup.zip", bool withLength = true)
    {
        HttpContent content = withLength ? new ByteArrayContent(body) : new StreamContent(new UnseekableStream(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return client.PutAsync($"/media/backups?name={Uri.EscapeDataString(name)}", content);
    }

    /// <summary>A body without a length: the client sends it in chunks, so only the server's own count can stop it.</summary>
    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public async Task TheOverview_ListsTheBackupsInTheFolder_AFileCopiedInByHandToo_WithTheSizesAndTheSettings()
    {
        using var app = App();
        var admin = await Admin(app);
        await admin.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }");
        var written = await WriteBackup(app);
        File.Copy(written, Path.Combine(_folder, "copied-in.zip"));
        File.WriteAllText(Path.Combine(_folder, "notes.zip"), "not a backup");

        var backups = (await admin.Gql(Overview)).Data().GetProperty("backups");

        Assert.Equal(Path.GetFullPath(_folder), backups.GetProperty("folder").GetString());
        var points = backups.GetProperty("restorePoints").EnumerateArray().ToList();
        Assert.Equal(["copied-in.zip", Path.GetFileName(written)], points.Select(p => p.GetProperty("file").GetString()).Order());
        var point = points[0];
        Assert.True(point.GetProperty("sameMigration").GetBoolean());
        Assert.False(point.GetProperty("olderMigration").GetBoolean());
        Assert.Equal(backups.GetProperty("migration").GetString(), point.GetProperty("migration").GetString());
        // In plain, so without the keys; the photos have a folder of their own on the test host and are only in a backup with Backups:Photos.
        Assert.Equal(("Sqlite", false, false, "LEFT_OUT", false), (point.GetProperty("provider").GetString(), point.GetProperty("encrypted").GetBoolean(), point.GetProperty("safetyNet").GetBoolean(),
            point.GetProperty("photos").GetString(), point.GetProperty("keys").GetBoolean()));
        Assert.True(point.GetProperty("rows").GetInt64() >= 2); // the administrator and the vehicle at least
        Assert.Equal(new FileInfo(written).Length, point.GetProperty("bytes").GetInt64());
        var sizes = backups.GetProperty("sizes");
        Assert.True(sizes.GetProperty("database").GetInt64() > 0);
        Assert.Equal(0, sizes.GetProperty("photos").GetInt64()); // the test host keeps its photos apart, and has none
        var settings = backups.GetProperty("settings");
        Assert.Equal((JsonValueKind.Null, true, false, 1024), (settings.GetProperty("recipient").ValueKind, settings.GetProperty("photosApart").GetBoolean(), settings.GetProperty("photos").GetBoolean(), settings.GetProperty("maxUploadMb").GetInt32()));
        Assert.False(backups.GetProperty("run").GetProperty("running").GetBoolean());
    }

    [Fact]
    public async Task ABackupOfAnOlderRelease_IsListedAsOlder_AndOneOfANewerRelease_AsNeither()
    {
        using var app = App();
        var admin = await Admin(app);
        var written = await WriteBackup(app);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "contracts", "backups", "older-migration-AddExpenses.zip"), Path.Combine(_folder, "older.zip"));
        var newer = Path.Combine(_folder, "newer.zip");
        File.Copy(written, newer);
        RewriteMigration(newer, BackupFile.ReadManifest(written).Migration!, "20990101000000_AddSomethingNewer");

        var points = (await admin.Gql(Overview)).Data().GetProperty("backups").GetProperty("restorePoints").EnumerateArray()
            .ToDictionary(p => p.GetProperty("file").GetString()!, p => (p.GetProperty("sameMigration").GetBoolean(), p.GetProperty("olderMigration").GetBoolean()));

        Assert.Equal((false, true), points["older.zip"]);
        Assert.Equal((false, false), points["newer.zip"]);
        Assert.Equal((true, false), points[Path.GetFileName(written)]);
    }

    /// <summary>The manifest of a backup file rewritten with another migration (the checksum covers the inner archive, not the manifest).</summary>
    private static void RewriteMigration(string file, string from, string to)
    {
        using var zip = System.IO.Compression.ZipFile.Open(file, System.IO.Compression.ZipArchiveMode.Update);
        var entry = zip.GetEntry("manifest.json")!;
        string json;
        using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
        writer.Write(json.Replace(from, to));
    }

    [Fact]
    public async Task OnlyAnAdministrator_SeesTheBackups_RunsOne_UploadsOrDownloads()
    {
        using var app = App();
        var people = await app.Users();
        var written = Path.GetFileName(await WriteBackup(app));

        Assert.Equal("auth.adminRequired", (await people.Alice.Gql(Overview)).ErrorKey());
        Assert.Equal("auth.adminRequired", (await people.Alice.Gql("mutation { runBackup }")).ErrorKey());
        Assert.Equal("auth.adminRequired", (await people.Alice.Gql("mutation { prepareBackupEncryption { recipient } }")).ErrorKey());
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(people.Alice, File.ReadAllBytes(Path.Combine(_folder, written)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await people.Alice.GetAsync($"/media/backups/{written}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync($"/media/backups/{written}")).StatusCode);
        Assert.Single(Directory.GetFiles(_folder)); // nothing uploaded
    }

    /// <summary>A writer that waits until it is let go: a run under way, for as long as the test needs one.</summary>
    private sealed class HeldWriter(IBackupWriter real) : IBackupWriter
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct)
        {
            await Release.Task.WaitAsync(ct);
            return await real.WriteAsync(request, ct);
        }
    }

    [Fact]
    public async Task ABackupWrittenNow_RunsInTheBackground_OneAtATime_AndTheOverviewFollowsIt()
    {
        HeldWriter? held = null;
        using var app = App(services: s => s.AddSingleton<IBackupWriter>(sp => held = new HeldWriter(ActivatorUtilities.CreateInstance<BackupWriter>(sp))));
        var admin = await Admin(app);

        Assert.True((await admin.Gql("mutation { runBackup }")).Data().GetProperty("runBackup").GetBoolean());
        Assert.False((await admin.Gql("mutation { runBackup }")).Data().GetProperty("runBackup").GetBoolean()); // one is under way: that is the backup asked for
        Assert.True((await admin.Gql(Overview)).Data().GetProperty("backups").GetProperty("run").GetProperty("running").GetBoolean());

        held!.Release.SetResult();
        await Idle(app);

        var backups = (await admin.Gql(Overview)).Data().GetProperty("backups");
        var run = backups.GetProperty("run");
        Assert.False(run.GetProperty("running").GetBoolean());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("error").ValueKind);
        var file = run.GetProperty("file").GetString()!;
        Assert.StartsWith("tankstat-backup-", file);
        Assert.Equal(file, Assert.Single(backups.GetProperty("restorePoints").EnumerateArray()).GetProperty("file").GetString());
        Assert.True(run.GetProperty("bytes").GetInt64() > 0);
        Assert.Contains(app.Log.From("Tankstat.Application.Backups.BackupAdminService"), e => e.Values.ContainsKey("UserId"));
    }

    [Fact]
    public async Task ABackupWrittenNow_IsWhatTheSettingsSay_EncryptedForTheirRecipient_WithThePhotos()
    {
        var recipient = AgeKeys.FormatRecipient(BackupKeys.Recipient(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        using var app = App(new()
        {
            ["Backups:Encryption:Recipient"] = recipient,
            ["Backups:Encryption:Salt"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            ["Backups:Photos"] = "true",
        });
        var admin = await Admin(app);

        (await admin.Gql("mutation { runBackup }")).Data();
        await Idle(app);

        var backups = (await admin.Gql(Overview)).Data().GetProperty("backups");
        Assert.Equal(recipient, backups.GetProperty("settings").GetProperty("recipient").GetString());
        var point = Assert.Single(backups.GetProperty("restorePoints").EnumerateArray());
        Assert.Equal((true, recipient, true, "SEPARATE", true), (point.GetProperty("encrypted").GetBoolean(), point.GetProperty("recipient").GetString(),
            point.GetProperty("currentRecipient").GetBoolean(), point.GetProperty("photos").GetString(), point.GetProperty("keys").GetBoolean()));
    }

    /// <summary>A writer that holds each run until it is let go, one release per run.</summary>
    private sealed class GatedWriter(IBackupWriter real) : IBackupWriter
    {
        public readonly SemaphoreSlim Go = new(0);
        public async Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct)
        {
            await Go.WaitAsync(ct);
            return await real.WriteAsync(request, ct);
        }
    }

    [Fact]
    public async Task WhileABackupIsWritten_TheOverviewSaysWhenThatOneStarted_NotHowTheLastOneWent()
    {
        GatedWriter? gated = null;
        using var app = App(services: s => s.AddSingleton<IBackupWriter>(sp => gated = new GatedWriter(ActivatorUtilities.CreateInstance<BackupWriter>(sp))));
        var admin = await Admin(app);
        const string Run = "{ backups { run { running startedAt finishedAt file bytes error } } }";
        (await admin.Gql("mutation { runBackup }")).Data();
        gated!.Go.Release();
        await Idle(app);
        var last = (await admin.Gql(Run)).Data().GetProperty("backups").GetProperty("run");

        (await admin.Gql("mutation { runBackup }")).Data();
        var underWay = (await admin.Gql(Run)).Data().GetProperty("backups").GetProperty("run");

        Assert.True(underWay.GetProperty("running").GetBoolean());
        Assert.True(underWay.GetProperty("startedAt").GetDateTimeOffset() >= last.GetProperty("finishedAt").GetDateTimeOffset());
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null),
            (underWay.GetProperty("finishedAt").ValueKind, underWay.GetProperty("file").ValueKind, underWay.GetProperty("bytes").ValueKind, underWay.GetProperty("error").ValueKind));
        gated.Go.Release();
        await Idle(app);
        var written = (await admin.Gql(Run)).Data().GetProperty("backups").GetProperty("run");
        Assert.Equal(underWay.GetProperty("startedAt").GetDateTimeOffset(), written.GetProperty("startedAt").GetDateTimeOffset());
        Assert.NotEqual(last.GetProperty("file").GetString(), written.GetProperty("file").GetString());
    }

    private sealed class CountingUsage : IStorageUsage
    {
        public int Calls;
        public Task<FileUsage> MeasureAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new FileUsage(1, null));
        }
    }

    [Fact]
    public async Task ThePolledStatus_DoesNotWalkTheUploadFolders_OnlyTheSizesDo()
    {
        var usage = new CountingUsage();
        using var app = App(services: s => s.AddSingleton<IStorageUsage>(usage));
        var admin = await Admin(app);

        for (var i = 0; i < 3; i++) (await admin.Gql("{ backups { folder run { running error } settings { maxUploadMb } } }")).Data();
        Assert.Equal(0, usage.Calls);

        Assert.Equal(1, (await admin.Gql("{ backups { sizes { pictures } } }")).Data().GetProperty("backups").GetProperty("sizes").GetProperty("pictures").GetInt64());
        Assert.Equal(1, usage.Calls);
    }

    [Fact]
    public async Task AFailedRun_SaysWhyInTheOverview()
    {
        using var app = App(services: s => s.AddSingleton<IBackupWriter>(new FailingWriter()));
        var admin = await Admin(app);

        (await admin.Gql("mutation { runBackup }")).Data();
        await Idle(app);

        var run = (await admin.Gql(Overview)).Data().GetProperty("backups").GetProperty("run");
        Assert.Equal(("backup.failed", JsonValueKind.Null), (run.GetProperty("error").GetString(), run.GetProperty("file").ValueKind));
        Assert.Contains(app.Log.From("Tankstat.Infrastructure.Backups.BackupRunner"), e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    private sealed class FailingWriter : IBackupWriter
    {
        public Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct) => throw new IOException("The disk is full.");
    }

    [Fact]
    public async Task AnUpload_IsStreamedIntoTheFolder_AndListed_AndDownloadsAsItWas()
    {
        using var app = App();
        var admin = await Admin(app);
        var written = await WriteBackup(app);
        var bytes = await File.ReadAllBytesAsync(written);
        File.Delete(written);

        var uploaded = await Upload(admin, bytes, "from the old server.zip", withLength: false);

        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var file = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync()).RootElement.GetProperty("file").GetString()!;
        Assert.Equal("uploaded-from-the-old-server.zip", file);
        var point = Assert.Single((await admin.Gql(Overview)).Data().GetProperty("backups").GetProperty("restorePoints").EnumerateArray());
        Assert.True(point.GetProperty("uploaded").GetBoolean());

        var download = await admin.GetAsync($"/media/backups/{file}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(file, download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/media/backups/missing.zip")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/media/backups/..%2Fsecret")).StatusCode);
        Assert.Contains(app.Log.From("Tankstat.Application.Backups.BackupAdminService"), e => e.Values.TryGetValue("File", out var f) && (string?)f == file);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUploadAboveTheLimit_IsRefused_AndLeavesNoPartialFile(bool withLength)
    {
        using var app = App(new() { ["Backups:MaxUploadMb"] = "1" });
        var admin = await Admin(app);
        var tooLarge = new byte[1024 * 1024 + 10];

        var refused = await Upload(admin, tooLarge, withLength: withLength);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
        var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(("backup.tooLarge", 1), (body.GetProperty("key").GetString(), body.GetProperty("args").GetProperty("maxMb").GetInt32()));
        Assert.True(!Directory.Exists(_folder) || Directory.GetFiles(_folder).Length == 0);
    }

    [Fact]
    public async Task AnUpload_AsksForAnAdministratorFirst_SoNobodyElseLearnsTheLimit()
    {
        using var app = App(new() { ["Backups:MaxUploadMb"] = "1" });
        var people = await app.Users();
        var tooLarge = new byte[1024 * 1024 + 10];

        var signedOut = await Upload(app.NewClient(), tooLarge);
        var alice = await Upload(people.Alice, tooLarge);

        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal((HttpStatusCode.Forbidden, "auth.adminRequired"), (alice.StatusCode, JsonDocument.Parse(await alice.Content.ReadAsStringAsync()).RootElement.GetProperty("key").GetString()));
        Assert.True(!Directory.Exists(_folder) || Directory.GetFiles(_folder).Length == 0);
    }

    [Fact]
    public async Task ADownload_IsAskedFirst_WithoutBeingLogged_AndNothingButABackupIsEverSent()
    {
        using var app = App();
        var people = await app.Users();
        var written = Path.GetFileName(await WriteBackup(app));
        new RestoreMarker(written, Guid.NewGuid(), DateTimeOffset.UtcNow, "AGE-SECRET-KEY-1ZZZZ").Write(_folder); // a restore waiting, its identity in plain
        HttpRequestMessage Head(string file) => new(HttpMethod.Head, $"/media/backups/{file}");

        Assert.Equal(HttpStatusCode.OK, (await people.Admin.SendAsync(Head(written))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await people.Admin.SendAsync(Head("missing.zip"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await people.Alice.SendAsync(Head(written))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().SendAsync(Head(written))).StatusCode);
        Assert.DoesNotContain(app.Log.From("Tankstat.Application.Backups.BackupAdminService"), e => e.Values.ContainsKey("File"));
        Assert.Equal(HttpStatusCode.NotFound, (await people.Admin.SendAsync(Head(RestoreMarker.FileName))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await people.Admin.GetAsync($"/media/backups/{RestoreMarker.FileName}")).StatusCode);
    }

    [Fact]
    public async Task ABackup_IsDeletedByAnAdministrator_NothingElseOfTheFolder_AndNothingWhileARestoreWaits()
    {
        using var app = App();
        var people = await app.Users();
        var written = Path.GetFileName(await WriteBackup(app));
        File.WriteAllText(Path.Combine(_folder, "notes.zip"), "not a backup");
        const string Delete = "mutation($f: String!) { deleteBackup(file: $f) }";

        Assert.Equal("auth.adminRequired", (await people.Alice.Gql(Delete, new { f = written })).ErrorKey());
        Assert.Equal("backup.corrupt", (await people.Admin.Gql(Delete, new { f = "notes.zip" })).ErrorKey()); // never listed: not a backup
        Assert.Equal("backup.notFound", (await people.Admin.Gql(Delete, new { f = "../" + written })).ErrorKey());
        new RestoreMarker(written, Guid.NewGuid(), DateTimeOffset.UtcNow, null).Write(_folder);
        Assert.Equal("backup.restorePending", (await people.Admin.Gql(Delete, new { f = written })).ErrorKey());
        Assert.Equal("backup.notFound", (await people.Admin.Gql(Delete, new { f = RestoreMarker.FileName })).ErrorKey());
        File.Delete(Path.Combine(_folder, RestoreMarker.FileName));

        Assert.True((await people.Admin.Gql(Delete, new { f = written })).Data().GetProperty("deleteBackup").GetBoolean());

        Assert.False(File.Exists(Path.Combine(_folder, written)));
        Assert.True(File.Exists(Path.Combine(_folder, "notes.zip")));
        Assert.Contains(app.Log.From("Tankstat.Application.Backups.BackupAdminService"), e => e.Values.TryGetValue("File", out var f) && Equals(f, written));
    }

    [Fact]
    public async Task AnUploadThatIsNoBackup_IsRefused_AndRemoved()
    {
        using var app = App();
        var admin = await Admin(app);

        var refused = await Upload(admin, System.Text.Encoding.UTF8.GetBytes("PK this is no zip"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("backup.corrupt", JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("key").GetString());
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AKeySet_IsMadeFromThePassphraseOrMadeUp_OpensItsRecipient_AndIsNeverLogged()
    {
        using var app = App();
        var admin = await Admin(app);
        const string Query = "mutation($p: String) { prepareBackupEncryption(passphrase: $p) { passphrase generated identity recipient salt } }";

        var typed = (await admin.Gql(Query, new { p = "zzcanary passphrase" })).Data().GetProperty("prepareBackupEncryption");
        var made = (await admin.Gql(Query, new { p = (string?)null })).Data().GetProperty("prepareBackupEncryption");

        Assert.Equal(("zzcanary passphrase", false), (typed.GetProperty("passphrase").GetString(), typed.GetProperty("generated").GetBoolean()));
        Assert.True(made.GetProperty("generated").GetBoolean());
        foreach (var set in new[] { typed, made })
        {
            var identity = AgeKeys.ParseIdentity(set.GetProperty("identity").GetString()!);
            Assert.True(BackupKeys.Opens(identity, set.GetProperty("recipient").GetString()!));
            Assert.Equal(identity, BackupKeys.Identity(set.GetProperty("passphrase").GetString()!, Convert.FromBase64String(set.GetProperty("salt").GetString()!), Argon2idParameters.Default));
        }
        Assert.Equal("backup.passphraseTooShort", (await admin.Gql(Query, new { p = "short" })).ErrorKey());
        foreach (var secret in new[] { "zzcanary", made.GetProperty("passphrase").GetString()!, typed.GetProperty("identity").GetString()!, made.GetProperty("identity").GetString()! })
            Assert.False(app.Log.Mentions(secret), "logged");
    }

    [Fact]
    public async Task ABackupUploadedOnThePage_AndRestoredFromIt_IsWhatTheRestartedHostServes()
    {
        // The whole way a backup takes on the page: written on one instance, downloaded, uploaded to another, restored there.
        string downloaded;
        using (var source = App())
        {
            var admin = await Admin(source);
            await admin.Gql("mutation { addVehicle(input: { name: \"Golf from before\", fuelType: PETROL }) { id } }");
            var file = Path.GetFileName(await WriteBackup(source));
            downloaded = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}.zip");
            await File.WriteAllBytesAsync(downloaded, await (await admin.GetAsync($"/media/backups/{file}")).Content.ReadAsByteArrayAsync());
        }
        Directory.Delete(_folder, recursive: true);
        try
        {
            using var shared = new SharedData();
            var target = shared.Host(TestApp.StandaloneSettings(new() { ["Backups:Path"] = _folder }));
            var admin = await Admin(target);
            await admin.Gql("mutation { addVehicle(input: { name: \"Own car\", fuelType: PETROL }) { id } }");
            var uploaded = JsonDocument.Parse(await (await Upload(admin, await File.ReadAllBytesAsync(downloaded))).Content.ReadAsStringAsync()).RootElement.GetProperty("file").GetString()!;
            var restore = await admin.Gql("mutation($i: RestoreBackupInput!) { restoreBackup(input: $i) { file } }", new { i = new { file = uploaded } });
            Assert.Equal(uploaded, restore.Data().GetProperty("restoreBackup").GetProperty("file").GetString());
            target.Dispose(); // the app stops itself after that answer; the restart policy starts it again

            using var restarted = shared.Host(TestApp.StandaloneSettings(new() { ["Backups:Path"] = _folder }));
            var again = await Admin(restarted);
            var names = (await again.Gql("{ myVehicles { name } }")).Data().GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToList();
            Assert.Equal(["Golf from before"], names);
        }
        finally
        {
            File.Delete(downloaded);
        }
    }
}
