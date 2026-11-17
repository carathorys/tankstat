using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Application.Imports;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Backups.Reader;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// A person's uploaded backup on its way to "opened" (#336): the session, its three tries, what a hostile file is answered, and that
/// whatever ends a session leaves nothing of it on disk.
/// </summary>
public sealed class BackupImportSessionsTests : IAsyncDisposable
{
    private const string Passphrase = "correct horse battery staple";
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("tankstat-salt-01");
    private static readonly Lazy<byte[]> Identity = new(() => BackupKeys.Identity(Passphrase, Salt, Argon2idParameters.Default));

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-sessions-{Guid.NewGuid():N}");
    private readonly SettableClock _clock = new();
    private readonly List<TestDatabase> _databases = [];
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>Sign-in is off in these instances: whoever asks is the anonymous user.</summary>
    private sealed class Nobody : Tankstat.Application.Auth.ICurrentUser
    {
        public Task<Tankstat.Application.Auth.Principal?> GetPrincipalAsync(CancellationToken ct) => Task.FromResult<Tankstat.Application.Auth.Principal?>(null);
    }

    private sealed class SettableClock : TimeProvider
    {
        public TimeSpan Ahead { get; set; }
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Ahead;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var database in _databases)
        {
            await database.Get<BackupImportSessions>().StopAsync(default);
            await database.DisposeAsync();
        }
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Scratch => Path.Combine(_root, "scratch", BackupImportSessions.FolderName);

    private TestDatabase Instance(bool encrypted = false, int maxUploadMb = 200)
    {
        var name = $"instance-{_databases.Count}";
        var database = new TestDatabase(new()
        {
            ["Storage:Path"] = Path.Combine(_root, name, "uploads"), ["DataProtection:KeysPath"] = Path.Combine(_root, name, "keys"),
            ["Backups:Path"] = Path.Combine(_root, name, "backups"),
            ["Backups:Encryption:Recipient"] = encrypted ? AgeKeys.FormatRecipient(BackupKeys.Recipient(Identity.Value)) : null,
            ["Backups:Encryption:Salt"] = encrypted ? Convert.ToBase64String(Salt) : null,
            ["Imports:ScratchFolder"] = Path.Combine(_root, "scratch"),
            ["Imports:MaxUploadMb"] = maxUploadMb.ToString(),
        }, services => services.AddSingleton<TimeProvider>(_clock).AddSingleton<Tankstat.Application.Auth.ICurrentUser>(new Nobody()));
        _databases.Add(database);
        return database;
    }

    private async Task<(TestDatabase Database, string Backup, BackupSeed.Seeded Seeded)> SeededAsync(bool encrypted = false, int maxUploadMb = 200)
    {
        var database = Instance(encrypted, maxUploadMb);
        var name = $"instance-{_databases.Count - 1}";
        var seeded = await BackupSeed.SeedAsync(database, Path.Combine(_root, name, "uploads"), Path.Combine(_root, name, "uploads"), Path.Combine(_root, name, "keys"));
        var backup = await database.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return (database, backup.Path, seeded);
    }

    private static async Task<BackupImportStatus> UploadAsync(TestDatabase database, Guid user, string file)
    {
        await using var content = File.OpenRead(file);
        return await database.Get<IBackupImportSessions>().UploadAsync(user, content, default);
    }

    /// <summary>Asks, as the page does, until the file is no longer being opened.</summary>
    private static async Task<BackupImportStatus> SettledAsync(TestDatabase database, Guid user, string token)
    {
        for (var i = 0; i < 600; i++)
        {
            var status = database.Get<IBackupImportSessions>().Find(user, token) ?? throw new InvalidOperationException("The session is gone.");
            if (status.State != BackupImportState.Opening)
            {
                // A refused file's session says so before its folder is gone: the open has wound down before a test looks at the scratch folder.
                await database.Get<BackupImportSessions>().WhenIdleAsync();
                return status;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The file was not opened.");
    }

    private string[] Folders() => Directory.Exists(Scratch) ? [.. Directory.GetDirectories(Scratch).Select(Path.GetFileName)!] : [];

    /// <summary>
    /// A copy of a backup with its manifest changed, and optionally its inner archive replaced (the manifest's checksum follows unless
    /// <paramref name="keepChecksum"/>), or with entries added to the inner archive of a plain backup.
    /// </summary>
    private string Forged(string backup, Action<JsonObject>? manifest = null, byte[]? archive = null, bool keepChecksum = false, Action<ZipArchive>? inner = null)
    {
        var forged = Path.Combine(_root, $"forged-{Guid.NewGuid():N}.zip");
        using var source = ZipFile.OpenRead(backup);
        var json = JsonNode.Parse(new StreamReader(source.GetEntry("manifest.json")!.Open()).ReadToEnd())!.AsObject();
        var name = json["archive"]!["name"]!.GetValue<string>();
        using (var data = source.GetEntry(name)!.Open())
        using (var copy = new MemoryStream())
        {
            data.CopyTo(copy);
            archive ??= copy.ToArray();
        }
        if (inner is not null)
        {
            using var changed = new MemoryStream();
            changed.Write(archive);
            using (var zip = new ZipArchive(changed, ZipArchiveMode.Update, leaveOpen: true)) inner(zip);
            archive = changed.ToArray();
        }
        if (!keepChecksum) json["archive"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(archive));
        manifest?.Invoke(json);
        using var target = ZipFile.Open(forged, ZipArchiveMode.Create);
        using (var entry = target.CreateEntry(name, CompressionLevel.Optimal).Open()) entry.Write(archive);
        using (var entry = target.CreateEntry("manifest.json").Open()) entry.Write(Encoding.UTF8.GetBytes(json.ToJsonString()));
        return forged;
    }

    [Fact]
    public async Task APlainBackup_IsOpenedInTheBackground_AndItsCarsAreRead_WithTheArchiveKeptAndNoRowsLeftUnpacked()
    {
        var (database, backup, seeded) = await SeededAsync();

        var uploaded = await UploadAsync(database, Alice, backup);
        var opened = await SettledAsync(database, Alice, uploaded.Token);

        Assert.Equal((BackupFileOrigin.Upload, BackupImportState.Opened, null, null), (opened.Origin, opened.State, opened.TriesLeft, opened.Refusal));
        var car = Assert.Single(opened.Summary!.Cars);
        Assert.Equal((seeded.Vehicle, BackupSeed.Name, BackupSeed.Plate, "Zz Canary", 1, 1, 0), (car.Id, car.Name, car.LicensePlate, car.OwnerName, car.Refuelings, car.Expenses, car.Schedules));
        Assert.False(opened.Summary.IsExport);
        var read = await database.Get<IBackupImportSessions>().Cars(Alice, uploaded.Token)!.ReadCarAsync(seeded.Vehicle, default);
        Assert.Equal((seeded.Refueling, BackupSeed.Day, "zzcanary note"), (read!.Refuelings.Single().Id, read.Refuelings.Single().Values.Date, read.Refuelings.Single().Values.Note));
        Assert.Equal((seeded.Expense, "Tyres", 100m, "EUR", 1500L), (read.Expenses.Single().Id, read.Expenses.Single().Values.Title, read.Expenses.Single().Values.Amount, read.Expenses.Single().Values.Currency, read.Expenses.Single().Values.Odometer));
        Assert.Null(await database.Get<IBackupImportSessions>().Cars(Alice, uploaded.Token)!.ReadCarAsync(Guid.NewGuid(), default));

        Assert.Equal([uploaded.Token], Folders());
        Assert.Equal(["cars.db", "data.zip", "upload.zip"], Directory.GetFileSystemEntries(Path.Combine(Scratch, uploaded.Token)).Select(Path.GetFileName).Where(f => !f!.StartsWith("cars.db-")).Order());
        // Someone else knows nothing of it.
        Assert.Null(database.Get<IBackupImportSessions>().Find(Bob, uploaded.Token));
        Assert.Null(database.Get<IBackupImportSessions>().Cars(Bob, uploaded.Token));
    }

    /// <summary>
    /// A car as people keep one, saved through the services: its schedules, a visit that marked one done (the expense's link), and what
    /// is in the trash, an entry and a whole car, which the reader leaves out and counts.
    /// </summary>
    [Fact]
    public async Task ACarsSchedulesAndAVisitsLink_AreRead_AndWhatWasInTheTrashIsLeftOut()
    {
        var database = Instance();
        var day = new DateOnly(2026, 3, 1);
        Guid car, kept, trashedFill, schedule, visit;
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var vehicles = services.GetRequiredService<Tankstat.Application.Vehicles.VehicleService>();
            car = (await vehicles.AddAsync("Octavia", "ABC-123", Tankstat.Domain.Vehicles.FuelType.Diesel, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default)).Id;
            var gone = await vehicles.AddAsync("Sold long ago", null, Tankstat.Domain.Vehicles.FuelType.Petrol, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default);
            await vehicles.DeleteAsync(gone.Id, default);
            var fills = services.GetRequiredService<Tankstat.Application.Refuelings.RefuelingService>();
            kept = (await fills.LogAsync(car, new(day, 40, 60, "EUR", 10_000, true, "kept"), default)).Id;
            trashedFill = (await fills.LogAsync(car, new(day.AddDays(9), 35, 52, "EUR", 10_600, true, "in the trash"), default)).Id;
            await fills.DeleteAsync(trashedFill, default);
            var recurring = services.GetRequiredService<Tankstat.Application.Recurring.RecurringExpenseService>();
            schedule = (await recurring.AddAsync(car, new("Oil change", "Service", null, Tankstat.Domain.Recurring.RecurrenceKind.Time, 12, null, day.AddYears(-1), null, null, null), default)).Item.Id;
            await recurring.AddAsync(car, new("Inspection", null, null, Tankstat.Domain.Recurring.RecurrenceKind.Time, 24, null, day, null, null, null), default);
            visit = (await recurring.MarkDoneAsync([schedule], new(day.AddDays(3), 10_200, 95, "EUR"), default)).Expense!.Id;
            await services.GetRequiredService<Tankstat.Application.Expenses.ExpenseService>().AddAsync(car, new(day.AddDays(1), "Car wash", null, 12, "EUR", null, null), default);
        }
        var backup = await database.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        var uploaded = await UploadAsync(database, Alice, backup.Path);
        var opened = await SettledAsync(database, Alice, uploaded.Token);

        var summary = Assert.Single(opened.Summary!.Cars);
        Assert.Equal((car, "Octavia", 1, 2, 2), (summary.Id, summary.Name, summary.Refuelings, summary.Expenses, summary.Schedules));
        Assert.Equal(1, opened.Summary.TrashedCars);
        var read = (await database.Get<IBackupImportSessions>().Cars(Alice, uploaded.Token)!.ReadCarAsync(car, default))!;
        Assert.Equal([kept], read.Refuelings.Select(r => r.Id)); // not the one in the trash
        Assert.Equal(["Inspection", "Oil change"], read.Schedules.Select(s => s.Values.Title));
        var oil = read.Schedules.Single(s => s.Id == schedule).Values;
        Assert.Equal(("Service", Tankstat.Domain.Recurring.RecurrenceKind.Time, 12, day.AddDays(3), 10_200L), (oil.Category, oil.Kind, oil.IntervalMonths, oil.LastDoneDate, oil.LastDoneOdometer));
        Assert.Equal([schedule], read.Expenses.Single(e => e.Id == visit).CompletedScheduleIds); // the visit's link
        Assert.Empty(read.Expenses.Single(e => e.Values.Title == "Car wash").CompletedScheduleIds);
        Assert.Equal((95m, "EUR", 10_200L), (read.Expenses.Single(e => e.Id == visit).Values.Amount, read.Expenses.Single(e => e.Id == visit).Values.Currency, read.Expenses.Single(e => e.Id == visit).Values.Odometer));
    }

    /// <summary>
    /// A car's rows are the uploader's and the scratch database enforces no lengths (#337): a text longer than the app itself allows was
    /// never written by it, so the car is refused as no backup when it is read, before its rows are loaded, and none of it is logged.
    /// </summary>
    [Theory]
    [InlineData("Refuelings", "kept")]
    [InlineData("Expenses", "Car wash")]
    public async Task ACarWithATextLongerThanTheAppAllows_IsNoBackup_WhenItIsRead(string table, string text)
    {
        var database = Instance();
        Guid car;
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            car = (await services.GetRequiredService<Tankstat.Application.Vehicles.VehicleService>()
                .AddAsync("Octavia", null, Tankstat.Domain.Vehicles.FuelType.Diesel, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default)).Id;
            await services.GetRequiredService<Tankstat.Application.Refuelings.RefuelingService>().LogAsync(car, new(new DateOnly(2026, 3, 1), 40, 60, "EUR", 10_000, true, "kept"), default);
            await services.GetRequiredService<Tankstat.Application.Expenses.ExpenseService>().AddAsync(car, new(new DateOnly(2026, 3, 2), "Car wash", null, 12, "EUR", null, null), default);
        }
        var backup = await database.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var forged = Forged(backup.Path, inner: zip =>
        {
            var entry = zip.GetEntry($"tables/{table}.jsonl")!;
            string rows;
            using (var reader = new StreamReader(entry.Open())) rows = reader.ReadToEnd();
            entry.Delete();
            using var written = zip.CreateEntry($"tables/{table}.jsonl").Open();
            written.Write(Encoding.UTF8.GetBytes(rows.Replace($"\"{text}\"", $"\"zzcanary{new string('x', 600)}\"")));
        });

        var uploaded = await UploadAsync(database, Alice, forged);
        var opened = await SettledAsync(database, Alice, uploaded.Token);
        Assert.Equal(BackupImportState.Opened, opened.State); // the open only counts the rows

        var refused = await Assert.ThrowsAsync<DomainException>(() => database.Get<IBackupImportSessions>().Cars(Alice, uploaded.Token)!.ReadCarAsync(car, default));

        Assert.Equal("import.notABackup", refused.Key);
        Assert.False(database.Log.Mentions("zzcanary"));
    }

    [Fact]
    public async Task AFileWithoutCars_AndACarWithoutAPlate_AreOpened()
    {
        var empty = Instance();
        var none = await empty.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var nothing = await SettledAsync(empty, Alice, (await UploadAsync(empty, Alice, none.Path)).Token);
        Assert.Equal(BackupImportState.Opened, nothing.State);
        Assert.Empty(nothing.Summary!.Cars);

        var database = Instance();
        await using (var scope = database.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Tankstat.Application.Vehicles.VehicleService>()
                .AddAsync("No plate yet", null, Tankstat.Domain.Vehicles.FuelType.Petrol, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default);
        var backup = await database.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var opened = await SettledAsync(database, Bob, (await UploadAsync(database, Bob, backup.Path)).Token);

        var car = Assert.Single(opened.Summary!.Cars);
        Assert.Equal(("No plate yet", null, 0), (car.Name, car.LicensePlate, car.Refuelings));
    }

    [Fact]
    public async Task AnEncryptedBackup_WaitsForItsPassphrase_ThreeWrongOnesRemoveTheFile_AndTheRightOneOpensIt()
    {
        var (database, backup, _) = await SeededAsync(encrypted: true);
        var sessions = database.Get<IBackupImportSessions>();

        var waiting = await UploadAsync(database, Alice, backup);
        Assert.Equal((BackupImportState.NeedsKey, 3, false), (waiting.State, waiting.TriesLeft, waiting.WrongKey));
        Assert.Throws<DomainException>(() => sessions.SubmitKey(Alice, waiting.Token, "", null)); // no try spent on nothing

        // An identity is not taken for a person's own upload (the owner's decision 3): it counts as no key at all.
        Assert.Equal("backup.keyRequired", Assert.Throws<DomainException>(() => sessions.SubmitKey(Alice, waiting.Token, null, AgeKeys.FormatIdentity(Identity.Value))).Key);

        sessions.SubmitKey(Alice, waiting.Token, "wrong one", null);
        var first = await SettledAsync(database, Alice, waiting.Token);
        Assert.Equal((BackupImportState.NeedsKey, 2, true), (first.State, first.TriesLeft, first.WrongKey));
        sessions.SubmitKey(Alice, waiting.Token, "wrong two", null);
        Assert.Equal(1, (await SettledAsync(database, Alice, waiting.Token)).TriesLeft);
        sessions.SubmitKey(Alice, waiting.Token, "wrong three", null);
        var ended = await SettledAsync(database, Alice, waiting.Token);

        Assert.Equal((BackupImportState.Ended, "import.noTriesLeft"), (ended.State, ended.Refusal!.Key));
        Assert.Empty(Folders()); // the file is gone from the server
        Assert.Equal(BackupImportState.Ended, sessions.SubmitKey(Alice, waiting.Token, Passphrase, null).State); // a fourth try finds nothing to open

        var again = await UploadAsync(database, Alice, backup);
        sessions.SubmitKey(Alice, again.Token, Passphrase, null);
        var opened = await SettledAsync(database, Alice, again.Token);
        Assert.Equal((BackupImportState.Opened, 1), (opened.State, opened.Summary!.Cars.Count));
    }

    [Fact]
    public async Task TwoTriesAtOnce_AreOneTry()
    {
        var (database, backup, _) = await SeededAsync(encrypted: true);
        var sessions = database.Get<IBackupImportSessions>();
        var waiting = await UploadAsync(database, Alice, backup);

        var first = sessions.SubmitKey(Alice, waiting.Token, "wrong", null);
        var second = sessions.SubmitKey(Alice, waiting.Token, "wrong again", null);

        Assert.Equal((BackupImportState.Opening, BackupImportState.Opening), (first.State, second.State));
        Assert.Equal(2, (await SettledAsync(database, Alice, waiting.Token)).TriesLeft);
    }

    [Fact]
    public async Task ARestorePoint_HasNoLimitOfTries_TakesAnIdentity_KeepsTheEmails_AndIsNeverDeleted()
    {
        var (database, backup, _) = await SeededAsync(encrypted: true);
        var sessions = database.Get<IBackupImportSessions>();

        var waiting = await sessions.OpenFileAsync(Alice, backup, default);
        Assert.Equal((BackupFileOrigin.RestorePoint, BackupImportState.NeedsKey, null), (waiting.Origin, waiting.State, waiting.TriesLeft));
        for (var i = 0; i < 4; i++)
        {
            sessions.SubmitKey(Alice, waiting.Token, null, "AGE-SECRET-KEY-not-one");
            Assert.Equal((BackupImportState.NeedsKey, true, null), ((await SettledAsync(database, Alice, waiting.Token)) is var s ? (s.State, s.WrongKey, s.TriesLeft) : default));
        }
        sessions.SubmitKey(Alice, waiting.Token, null, AgeKeys.FormatIdentity(Identity.Value));
        Assert.Equal(BackupImportState.Opened, (await SettledAsync(database, Alice, waiting.Token)).State);

        sessions.End(Alice, waiting.Token);
        Assert.True(File.Exists(backup));
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task ASecondUpload_ReplacesTheFirst_WhoseFolderGoesAtOnce_UnlessARunHoldsIt()
    {
        var (database, backup, _) = await SeededAsync();
        var sessions = database.Get<IBackupImportSessions>();
        var first = await UploadAsync(database, Alice, backup);
        await SettledAsync(database, Alice, first.Token);
        var bobs = await UploadAsync(database, Bob, backup);
        await SettledAsync(database, Bob, bobs.Token);

        var second = await UploadAsync(database, Alice, backup);
        await SettledAsync(database, Alice, second.Token);

        Assert.Null(sessions.Find(Alice, first.Token));
        Assert.Equal(new[] { bobs.Token, second.Token }.Order(), Folders().Order()); // one per person

        using (sessions.Hold(Alice, second.Token))
        {
            Assert.Equal("import.fileInUse", (await Assert.ThrowsAsync<DomainException>(() => UploadAsync(database, Alice, backup))).Key);
            _clock.Ahead = TimeSpan.FromHours(2);
            database.Get<BackupImportSessions>().SweepNow();
            Assert.NotNull(sessions.Cars(Alice, second.Token)); // a run reads it: neither replaced nor expired
            sessions.End(Alice, second.Token); // the person leaves the step: the file is the run's until it lets go
            Assert.NotNull(sessions.Cars(Alice, second.Token));
        }
        Assert.Equal(new[] { second.Token }, Folders()); // Bob's expired meanwhile, and the refused upload left nothing
    }

    /// <summary>
    /// In the system's temporary folder the scratch root has a name anyone on the machine can make first: only a real folder this
    /// account owns is used (a link is refused; a folder of another account fails the same check, which a test cannot stage).
    /// </summary>
    [Fact]
    public void TheDefaultScratchRoot_MustBeARealFolderOfThisAccount_NeverALink()
    {
        if (OperatingSystem.IsWindows()) return;
        var real = Directory.CreateDirectory(Path.Combine(_root, "real", "tankstat-import")).FullName;
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, real);

        Assert.True(BackupImportSessions.IsOwnFolder(real));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(real)); // whatever it was made with
        Assert.False(BackupImportSessions.IsOwnFolder(link));
        Assert.False(BackupImportSessions.IsOwnFolder(Path.Combine(_root, "missing")));
    }

    [Fact]
    public async Task ASessionExpiresHalfAnHourAfterItsLastUse_AndTheAccountsDeletionEndsIt()
    {
        var (database, backup, _) = await SeededAsync();
        var sessions = database.Get<IBackupImportSessions>();
        var store = database.Get<BackupImportSessions>();
        var mine = await UploadAsync(database, Alice, backup);
        await SettledAsync(database, Alice, mine.Token);

        _clock.Ahead = TimeSpan.FromMinutes(29);
        store.SweepNow();
        Assert.NotNull(sessions.Find(Alice, mine.Token)); // asked: used again
        _clock.Ahead = TimeSpan.FromMinutes(58);
        store.SweepNow();
        Assert.Equal([mine.Token], Folders());
        _clock.Ahead = TimeSpan.FromMinutes(58 + 31);
        store.SweepNow();
        Assert.Null(sessions.Find(Alice, mine.Token));
        Assert.Empty(Folders());

        var bobs = await UploadAsync(database, Bob, backup);
        sessions.EndForUser(Bob); // while it is still being opened
        Assert.Null(sessions.Find(Bob, bobs.Token));
        await store.WhenIdleAsync(); // the open winds down: what it wrote under the folder's delete, or after it, is gone too
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task AStart_EmptiesTheScratchFolder_OfWhatAStoppedServerLeft()
    {
        var database = Instance();
        Directory.CreateDirectory(Path.Combine(Scratch, "left-behind"));
        File.WriteAllText(Path.Combine(Scratch, "left-behind", "upload.zip"), "someone's cars");
        File.WriteAllText(Path.Combine(_root, "scratch", "not-ours.txt"), "another program's");

        await database.Get<BackupImportSessions>().StartAsync(default);

        Assert.Empty(Folders());
        Assert.True(File.Exists(Path.Combine(_root, "scratch", "not-ours.txt"))); // only the app's own subfolder is emptied
    }

    [Fact]
    public async Task AnUploadPastTheLimit_IsRefused_WithNothingLeft()
    {
        var database = Instance(maxUploadMb: 1);
        await using var large = new MemoryStream(new byte[1024 * 1024 + 1]);

        var refused = await Assert.ThrowsAsync<DomainException>(() => database.Get<IBackupImportSessions>().UploadAsync(Alice, large, default));

        Assert.Equal(("import.tooLarge", 1), (refused.Key, refused.Args["maxMb"]));
        Assert.Empty(Folders());
    }

    public static TheoryData<string> NotBackups => new("not a zip", "zip without a manifest", "another program's manifest", "greedy passphrase costs");

    [Theory, MemberData(nameof(NotBackups))]
    public async Task WhatIsNoBackup_IsRefusedAtTheUpload_BeforeAnyKeyIsWorkedOn_WithNothingLeft(string what)
    {
        var (database, backup, _) = await SeededAsync(encrypted: true);
        var file = Path.Combine(_root, "hostile.zip");
        switch (what)
        {
            case "not a zip": File.WriteAllText(file, "PK? no."); break;
            case "zip without a manifest":
                using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) zip.CreateEntry("data.zip").Open().Dispose();
                break;
            case "another program's manifest":
                using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
                using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write("""{"manifest_version":3,"name":"An extension"}"""u8);
                break;
            default:
                // Twice the memory this release ever asks for: the file's word, which would be paid before any key is tried.
                file = Forged(backup, m => m["encryption"]!["argon2id"]!["memoryKiB"] = 2 * Argon2idParameters.Default.MemoryKiB);
                break;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var refused = await Assert.ThrowsAsync<DomainException>(() => UploadAsync(database, Alice, file));

        Assert.Equal("import.notABackup", refused.Key);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "Nothing was derived.");
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task ABackupOfANewerRelease_IsSaidSo_WithItsVersionCutToARelease()
    {
        var (database, backup, _) = await SeededAsync();
        var newerMigration = Forged(backup, m => { m["migration"] = "20991231235959_FromTheFuture"; m["appVersion"] = "9.9.9-<script>alert(1)</script>" + new string('9', 200); });
        var higherFormat = Forged(backup, m => m["format"] = 99);

        foreach (var file in new[] { newerMigration, higherFormat })
        {
            var refused = await Assert.ThrowsAsync<DomainException>(() => UploadAsync(database, Alice, file));
            Assert.Equal("backup.newerFormat", refused.Key);
            var version = (string)refused.Args["appVersion"]!;
            Assert.True(version.Length <= 40 && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+'), version);
        }
        Assert.Empty(Folders());
    }

    public static TheoryData<string> BrokenInside => new("an archive that inflates past the limit", "a checksum that does not match", "fewer rows than the manifest says",
        "a row no column takes", "an inner archive that is no zip", "a row longer than a megabyte");

    [Theory, MemberData(nameof(BrokenInside))]
    public async Task AFileThatBreaksWhileItIsOpened_EndsAsNoBackup_WithNothingLeft_AndItsTextIsNotLogged(string what)
    {
        var (database, backup, _) = await SeededAsync(maxUploadMb: 2);
        var file = what switch
        {
            // Three megabytes of zeros deflate to a few kilobytes: counted as they come out, never by the header's word.
            "an archive that inflates past the limit" => Forged(backup, archive: new byte[3 * 1024 * 1024]),
            "a checksum that does not match" => Forged(backup, archive: [.. File.ReadAllBytes(backup).Take(64)], keepChecksum: true),
            "fewer rows than the manifest says" => Forged(backup, m => m["contents"]!["tables"]!["Refuelings"] = 7),
            "a row no column takes" => Forged(backup, inner: zip =>
            {
                zip.GetEntry("tables/Vehicles.jsonl")!.Delete();
                using var rows = zip.CreateEntry("tables/Vehicles.jsonl").Open();
                rows.Write("""{"Id":"zzcanary-not-an-id","Name":"Zzcanary forged car"}"""u8);
            }),
            "a row longer than a megabyte" => Forged(backup, inner: zip =>
            {
                zip.GetEntry("tables/Vehicles.jsonl")!.Delete();
                using var rows = zip.CreateEntry("tables/Vehicles.jsonl").Open();
                rows.Write(Encoding.UTF8.GetBytes($$"""{"Id":"{{Guid.NewGuid()}}","Name":"zzcanary{{new string('x', 1024 * 1024)}}"}"""));
            }),
            _ => Forged(backup, archive: "zzcanary: this is no zip"u8.ToArray()),
        };

        var uploaded = await UploadAsync(database, Alice, file);
        var ended = await SettledAsync(database, Alice, uploaded.Token);

        Assert.Equal((BackupImportState.Ended, "import.notABackup"), (ended.State, ended.Refusal!.Key));
        Assert.Empty(Folders());
        Assert.False(database.Log.Mentions("zzcanary")); // whatever failed, none of the file's text is in the log
    }

    [Fact]
    public async Task OnlyTheModelsOwnTableFilesAreWritten_NeverANameFromTheArchive_NorTheServersKeys()
    {
        var (database, backup, _) = await SeededAsync();
        var file = Forged(backup, inner: zip =>
        {
            foreach (var name in new[] { "tables/../../escaped.jsonl", "../escaped.txt", "tables/Evil.jsonl", "keys/key-1.xml", "files/pictures/x" })
                using (var entry = zip.CreateEntry(name).Open()) entry.Write("zzcanary"u8);
        });

        var uploaded = await UploadAsync(database, Alice, file);
        var opened = await SettledAsync(database, Alice, uploaded.Token);

        Assert.Equal(BackupImportState.Opened, opened.State);
        var written = Directory.GetFiles(Path.Combine(_root, "scratch"), "*", SearchOption.AllDirectories).Select(Path.GetFileName).Where(f => !f!.StartsWith("cars.db")).Order();
        Assert.Equal(["data.zip", "upload.zip"], written);
        Assert.Empty(Directory.GetFiles(_root, "escaped*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AnExport_IsReadHere_AndRefusedByEverythingElseThatReadsAManifest()
    {
        var (database, backup, _) = await SeededAsync();
        var export = Forged(backup, m => m["writtenBy"] = BackupManifest.Export);

        var uploaded = await UploadAsync(database, Alice, export);
        var opened = await SettledAsync(database, Alice, uploaded.Token);

        Assert.True(opened.Summary!.IsExport);
        Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => BackupFile.ReadManifest(export)).Key);
    }
}
