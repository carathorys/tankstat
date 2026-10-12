using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Images;
using Tankstat.Application.Photos;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Backups;
using Tankstat.Domain.Images;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// A backup of a SQLite instance: every table as typed JSON Lines from a consistent copy, the files of the roots (never the drafts), the keys
/// only when encrypted, a manifest in plain without anything personal, and a file the standard tools and the age reader open.
/// </summary>
public sealed class BackupWriterTests : IDisposable
{
    private static readonly DateOnly Day = BackupSeed.Day;
    private const string Email = BackupSeed.Email;
    private const string Name = BackupSeed.Name;
    private const string Plate = BackupSeed.Plate;

    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-backup-{Guid.NewGuid():N}");
    private string Pictures => Path.Combine(_data, "uploads");
    private string Photos => Path.Combine(_data, "photos");
    private string Keys => Path.Combine(_data, "keys");
    private string Backups => Path.Combine(_data, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }

    private TestDatabase Database(bool separatePhotos = false, string? recipient = null, bool photos = false) => new(new Dictionary<string, string?>
    {
        ["Storage:Path"] = Pictures,
        ["Storage:PhotosPath"] = separatePhotos ? Photos : null,
        ["DataProtection:KeysPath"] = Keys,
        ["Backups:Path"] = Backups,
        ["Backups:Photos"] = photos ? "true" : null,
        ["Backups:Encryption:Recipient"] = recipient,
        ["Backups:Encryption:Salt"] = recipient is null ? null : Convert.ToBase64String(Encoding.ASCII.GetBytes("tankstat-salt-01")),
    });

    private Task<BackupSeed.Seeded> Seed(TestDatabase db, bool separatePhotos) => BackupSeed.SeedAsync(db, Pictures, separatePhotos ? Photos : Pictures, Keys);

    private static string Manifest(string file) => BackupSeed.Manifest(file);
    private static string Inner(string file, byte[]? identity = null) => BackupSeed.Inner(file, identity);
    private static string[] Lines(string folder, string table) => BackupSeed.Lines(folder, table);
    private static IEnumerable<string> Files(string folder) => BackupSeed.Files(folder);

    [Fact]
    public async Task APlainBackup_HoldsEveryTable_TheFilesButTheDrafts_NoKeys_AndAManifestWithoutAnythingPersonal()
    {
        await using var db = Database();
        var seeded = await Seed(db, separatePhotos: false);

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        Assert.StartsWith(Backups + Path.DirectorySeparatorChar + "tankstat-backup-", result.Path);
        Assert.Equal([Path.GetFileName(result.Path)], Directory.GetFiles(Backups).Select(Path.GetFileName)); // nothing partial or temporary left
        var manifest = BackupManifest.FromJson(Manifest(result.Path));
        Assert.Equal(result.Manifest.ToJson(), manifest.ToJson());
        Assert.Equal((1, "tankstat", "Sqlite", false, null), (manifest.Format, manifest.WrittenBy, manifest.Provider, manifest.SafetyNet, manifest.Encryption));
        Assert.Equal((BackupArchive.Plain, true, BackupPhotos.WithPictures, false), (manifest.Archive.Name, manifest.Contents.Pictures, manifest.Contents.Photos, manifest.Contents.Keys));

        // Every table of the model, by its name in the database, plus the migrations history; every DbSet is among them.
        await using (var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal(context.Database.GetMigrations().Last(), manifest.Migration); // the migration the database is at
            var tables = context.Model.GetEntityTypes().Select(e => e.GetTableName()).Where(t => t is not null).Distinct().ToList();
            Assert.True(tables.Count >= 27, "the model's tables");
            Assert.Equal(tables.Append(TableDump.MigrationsHistory).Order(), manifest.Contents.Tables.Keys.Order());
            Assert.Contains("InstanceState", manifest.Contents.Tables.Keys); // the generation row (made at start, so none here), which a restore stamps anew after loading
            Assert.Equal(context.Database.GetMigrations().Count(), manifest.Contents.Tables[TableDump.MigrationsHistory]);
        }
        Assert.Equal((1L, 1L, 1L, 1L, 4L, 2L), (manifest.Contents.Tables["Users"], manifest.Contents.Tables["Vehicles"], manifest.Contents.Tables["Refuelings"],
            manifest.Contents.Tables["Expenses"], manifest.Contents.Tables["Images"], manifest.Contents.Tables["OdometerReadings"]));

        var inner = Inner(result.Path);
        try
        {
            // The files: the pictures root as a whole (the photos are in it), except the drafts; no keys in a plain backup.
            Assert.Equal(
                [
                    "files/pictures/legacy-file-id",
                    $"files/pictures/users/{seeded.Owner:N}/{Files(inner).Single(f => f.Contains("/users/")).Split('/')[^1]}",
                    $"files/pictures/vehicles/{seeded.Vehicle:N}/picture/{seeded.Picture:N}",
                    $"files/pictures/vehicles/{seeded.Vehicle:N}/refuelings/{seeded.Refueling:N}/{seeded.Photo:N}",
                ],
                Files(inner).Where(f => f.StartsWith("files/")));
            Assert.Equal("PHO", File.ReadAllText(Path.Combine(inner, "files", "pictures", "vehicles", seeded.Vehicle.ToString("N"), "refuelings", seeded.Refueling.ToString("N"), seeded.Photo.ToString("N"))));
            Assert.DoesNotContain(Files(inner), f => f.StartsWith("keys/"));
            Assert.Equal(manifest.Contents.Tables.Count, Files(inner).Count(f => f.StartsWith("tables/")));
            Assert.Equal(3 * 4, manifest.Sizes.Pictures);
            Assert.Equal((0, 0), (manifest.Sizes.Photos, manifest.Sizes.Keys));

            // Typed rows: GUIDs as strings, dates as ISO 8601 UTC, numbers as numbers, booleans as booleans, null as null.
            var vehicle = JsonDocument.Parse(Assert.Single(Lines(inner, "Vehicles"))).RootElement;
            Assert.Equal(seeded.Vehicle, Guid.Parse(vehicle.GetProperty("Id").GetString()!));
            Assert.Equal(Name, vehicle.GetProperty("Name").GetString());
            Assert.Equal("Kilometers", vehicle.GetProperty("OdometerUnit").GetString()); // the owned units share the table
            Assert.Equal(JsonValueKind.Null, vehicle.GetProperty("DeletedAt").ValueKind);
            Assert.Equal(1, vehicle.GetProperty("Version").GetInt32());
            Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z$", vehicle.GetProperty("UpdatedAt").GetString());
            var refueling = JsonDocument.Parse(Assert.Single(Lines(inner, "Refuelings"))).RootElement;
            Assert.Equal(JsonValueKind.True, refueling.GetProperty("IsFullTank").ValueKind);
            Assert.Equal(40m, refueling.GetProperty("Volume").GetDecimal());
            Assert.Equal("2026-09-01", refueling.GetProperty("Date").GetString());
            Assert.Equal(seeded.Refueling, Guid.Parse(refueling.GetProperty("Id").GetString()!));
            var migration = JsonDocument.Parse(Lines(inner, TableDump.MigrationsHistory).Last()).RootElement;
            Assert.Equal(manifest.Migration, migration.GetProperty("MigrationId").GetString());
            Assert.Equal(manifest.Sizes.Tables, Directory.EnumerateFiles(Path.Combine(inner, "tables")).Sum(f => new FileInfo(f).Length));
        }
        finally
        {
            Directory.Delete(inner, recursive: true);
        }

        // The manifest and the log carry nothing personal: no e-mail, name, plate or note, no path of a user.
        var json = Manifest(result.Path);
        foreach (var needle in new[] { Email, Name, Plate, "zzcanary", "Zz Canary", seeded.Owner.ToString("N") }) Assert.DoesNotContain(needle, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_data, json);
        var line = Assert.Single(db.Log.From<BackupWriter>().Where(e => e.Level == LogLevel.Information));
        Assert.Equal(Path.GetFileName(result.Path), line.Values["File"]);
        Assert.Equal(manifest.Contents.Tables.Count, line.Values["Tables"]);
        foreach (var needle in new[] { Email, Name, Plate, "zzcanary", "zzsecret" }) Assert.False(db.Log.Mentions(needle), needle);
    }

    [Fact]
    public async Task EveryColumnOfEveryTable_IsInTheDump_AsTheDatabaseNamesIt()
    {
        await using var db = Database();
        await using var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        foreach (var table in TableDump.Tables(context.Model))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table.Name}\")";
            var columns = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            Assert.NotEmpty(columns);
            Assert.Equal(columns.Order(), table.Columns.Select(c => c.Name).Order()); // a column the model knows but the dump would drop shows up here
        }
    }

    [Fact]
    public async Task AnEncryptedBackup_HoldsTheKeys_IsOpenedByTheIdentityOnly_AndItsManifestNamesTheRecipientAndTheSalt()
    {
        var identity = BackupKeys.Identity("correct horse battery staple", Encoding.ASCII.GetBytes("tankstat-salt-01"), new Argon2idParameters(256, 1, 1));
        var recipient = AgeKeys.FormatRecipient(BackupKeys.Recipient(identity));
        await using var db = Database(recipient: recipient);
        await Seed(db, separatePhotos: false);

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        var manifest = result.Manifest;
        Assert.Equal(new BackupEncryption("age-x25519", recipient, Convert.ToBase64String(Encoding.ASCII.GetBytes("tankstat-salt-01")), Argon2idParameters.Default), manifest.Encryption);
        Assert.Equal((BackupArchive.Encrypted, true), (manifest.Archive.Name, manifest.Contents.Keys));
        Assert.True(manifest.Sizes.Keys > 0);
        using (var zip = ZipFile.OpenRead(result.Path))
        {
            Assert.Equal([BackupArchive.Encrypted, BackupManifest.FileName], zip.Entries.Select(e => e.FullName)); // the manifest last, found by name
            Assert.All(zip.Entries, e => Assert.Equal(e.Length, e.CompressedLength)); // stored, not compressed
            var data = zip.GetEntry(BackupArchive.Encrypted)!;
            Assert.Equal(manifest.Sizes.Archive, data.Length);
            using var stream = data.Open();
            Assert.Equal(manifest.Archive.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)));
            var head = new byte[22];
            using var again = data.Open();
            again.ReadExactly(head);
            Assert.Equal("age-encryption.org/v1\n", Encoding.ASCII.GetString(head));
        }
        var inner = Inner(result.Path, identity);
        try
        {
            var key = Assert.Single(Files(inner).Where(f => f.StartsWith("keys/")));
            Assert.StartsWith("keys/key-", key);
            Assert.Equal("<key>zzsecret</key>", File.ReadAllText(Path.Combine(inner, key)));
            Assert.Single(Lines(inner, "Vehicles"));
        }
        finally
        {
            Directory.Delete(inner, recursive: true);
        }
        var other = BackupKeys.Identity("wrong", Encoding.ASCII.GetBytes("tankstat-salt-01"), new Argon2idParameters(256, 1, 1));
        Assert.Equal("backup.wrongKey", Assert.Throws<Domain.DomainException>(() => Inner(result.Path, other)).Key);
        Assert.False(db.Log.Mentions("zzsecret"));
        Assert.False(db.Log.Mentions(AgeKeys.FormatIdentity(identity)));
    }

    [Theory]
    [InlineData(false, false, BackupPhotos.LeftOut)]
    [InlineData(false, true, BackupPhotos.Separate)] // Backups:Photos
    [InlineData(true, false, BackupPhotos.Separate)] // asked for this run
    public async Task APhotosRootOfItsOwn_GoesIn_OnlyWhenAskedFor_AndNeverItsDrafts(bool requested, bool configured, BackupPhotos expected)
    {
        await using var db = Database(separatePhotos: true, photos: configured);
        var seeded = await Seed(db, separatePhotos: true);

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(Photos: requested ? true : null), default);

        Assert.Equal(expected, result.Manifest.Contents.Photos);
        var inner = Inner(result.Path);
        try
        {
            var photos = Files(inner).Where(f => f.StartsWith("files/photos/")).ToList();
            if (expected == BackupPhotos.Separate)
            {
                Assert.Equal([$"files/photos/vehicles/{seeded.Vehicle:N}/refuelings/{seeded.Refueling:N}/{seeded.Photo:N}"], photos);
                Assert.Equal(3, result.Manifest.Sizes.Photos);
            }
            else
            {
                Assert.Empty(photos);
                Assert.Equal(0, result.Manifest.Sizes.Photos);
            }
            Assert.Equal(3, Files(inner).Count(f => f.StartsWith("files/pictures/"))); // the picture, the avatar, the legacy file
            Assert.DoesNotContain(Files(inner), f => f.Contains("/drafts/"));
        }
        finally
        {
            Directory.Delete(inner, recursive: true);
        }
    }

    [Fact]
    public async Task ANamedOutput_IsWrittenThere_AndAFailureLeavesNoPartialFile()
    {
        await using var db = Database();
        await Seed(db, separatePhotos: false);
        var output = Path.Combine(_data, "elsewhere", "before-upgrade.zip");

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(Output: output, SafetyNet: true), default);

        Assert.Equal(output, result.Path);
        Assert.True(result.Manifest.SafetyNet);
        Assert.Equal(["before-upgrade.zip"], Directory.GetFiles(Path.GetDirectoryName(output)!).Select(Path.GetFileName));

        // The same name again: refused (never overwritten), and nothing partial stays behind.
        await Assert.ThrowsAsync<IOException>(() => db.Get<IBackupWriter>().WriteAsync(new BackupRequest(Output: output), default));
        Assert.Equal(["before-upgrade.zip"], Directory.GetFiles(Path.GetDirectoryName(output)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ABackupWhileRowsAreWritten_HoldsEveryRowWhole_AndNoRowWithoutItsVehicle()
    {
        await using var db = Database();
        var seeded = await Seed(db, separatePhotos: false);
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(async () =>
        {
            var refuelings = db.Get<IRefuelingRepository>();
            for (var i = 2; !stop.IsCancellationRequested; i++)
            {
                var day = Day.AddDays(i);
                await refuelings.AddAsync(TestData.Refueling(seeded.Owner, seeded.Owner, seeded.Vehicle, day, odometer: 1000 + 100L * i), CancellationToken.None);
            }
        });
        var results = new List<BackupResult>();
        for (var i = 0; i < 3; i++) results.Add(await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default));
        stop.Cancel();
        await writer;

        foreach (var result in results)
        {
            var inner = Inner(result.Path);
            try
            {
                var refuelings = Lines(inner, "Refuelings").Select(l => JsonDocument.Parse(l).RootElement).ToList(); // every line parses: no row is cut
                var readings = Lines(inner, "OdometerReadings").Select(l => JsonDocument.Parse(l).RootElement.GetProperty("Id").GetString()).ToHashSet();
                var costs = Lines(inner, "Costs").Select(l => JsonDocument.Parse(l).RootElement.GetProperty("Id").GetString()).ToHashSet();
                Assert.Equal(result.Manifest.Contents.Tables["Refuelings"], refuelings.Count);
                Assert.All(refuelings, r =>
                {
                    Assert.Equal(seeded.Vehicle, Guid.Parse(r.GetProperty("VehicleId").GetString()!));
                    Assert.Contains(r.GetProperty("OdometerReadingId").GetString(), readings); // its reading and cost are in the same snapshot
                    Assert.Contains(r.GetProperty("CostId").GetString(), costs);
                });
            }
            finally
            {
                Directory.Delete(inner, recursive: true);
            }
        }
        Assert.True(results[^1].Manifest.Contents.Tables["Refuelings"] >= results[0].Manifest.Contents.Tables["Refuelings"]);
        Assert.Equal(3, Directory.GetFiles(Backups).Length); // three in the same second: three names
    }
}
