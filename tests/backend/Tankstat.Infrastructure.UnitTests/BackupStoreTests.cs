using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Backups;
using Tankstat.Application.Images;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;
using Tankstat.Infrastructure.Storage;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The backups folder as the administration page uses it: the restore points in it, uploads into it, downloads out of it.</summary>
public sealed class BackupStoreTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-store-{Guid.NewGuid():N}");
    private string Backups => Path.Combine(_data, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }

    private FileBackupStore Store() => new(Options.Create(new BackupOptions { Path = Backups }), new NoDatabase());

    /// <summary>The folder's tests never ask the database (only the migration check of a restore does): a factory that says so if it is used.</summary>
    private sealed class NoDatabase : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("The backups folder's tests use no database.");
    }

    /// <summary>A real backup file (of a seeded SQLite instance), written outside the backups folder.</summary>
    private async Task<string> BackupFileAsync()
    {
        await using var db = new TestDatabase(new Dictionary<string, string?>
        {
            ["Storage:Path"] = Path.Combine(_data, "uploads"),
            ["DataProtection:KeysPath"] = Path.Combine(_data, "keys"),
            ["Backups:Path"] = Path.Combine(_data, "elsewhere"),
        });
        await BackupSeed.SeedAsync(db, Path.Combine(_data, "uploads"), Path.Combine(_data, "uploads"), Path.Combine(_data, "keys"));
        return (await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default)).Path;
    }

    private static string Key(Func<Task> action) => Assert.ThrowsAnyAsync<KeyedException>(action).GetAwaiter().GetResult().Key;

    [Fact]
    public async Task TheRestorePoints_AreEveryReadableBackup_NewestFirst_AndNothingElseInTheFolder()
    {
        var backup = await BackupFileAsync();
        Directory.CreateDirectory(Backups);
        File.Copy(backup, Path.Combine(Backups, "copied-by-hand.zip"));
        await File.WriteAllTextAsync(Path.Combine(Backups, "notes.zip"), "not a zip at all");
        await File.WriteAllTextAsync(Path.Combine(Backups, "half.zip.partial"), "a backup being written");
        await File.WriteAllTextAsync(Path.Combine(Backups, "restore.refused.20261011T000000Z"), "{}");
        var store = Store();

        var point = Assert.Single(store.List());

        Assert.Equal(("copied-by-hand.zip", new FileInfo(backup).Length, false), (point.File, point.Bytes, point.Uploaded));
        Assert.Equal(Path.GetFullPath(Backups), store.Folder);
        Assert.Empty(new FileBackupStore(Options.Create(new BackupOptions { Path = Path.Combine(_data, "no-such-folder") }), new NoDatabase()).List());
    }

    [Fact]
    public async Task AnUpload_IsStoredUnderANameOfItsOwn_ThatNeverReplacesAFile()
    {
        var backup = await BackupFileAsync();
        var store = Store();

        var first = await store.SaveUploadAsync(File.OpenRead(backup), "My backup (old)!.zip", long.MaxValue, default);
        var second = await store.SaveUploadAsync(File.OpenRead(backup), "My backup (old)!.zip", long.MaxValue, default);
        var nameless = await store.SaveUploadAsync(File.OpenRead(backup), null, long.MaxValue, default);
        var sneaky = await store.SaveUploadAsync(File.OpenRead(backup), "../../etc/passwd", long.MaxValue, default);

        Assert.Equal(["uploaded-My-backup--old.zip", "uploaded-My-backup--old-2.zip", "uploaded-backup.zip", "uploaded-passwd.zip"], new[] { first.File, second.File, nameless.File, sneaky.File });
        Assert.All(new[] { first, second, nameless, sneaky }, p => Assert.True(p.Uploaded));
        Assert.Equal(4, store.List().Count);
        Assert.Equal(await File.ReadAllBytesAsync(backup), await File.ReadAllBytesAsync(Path.Combine(Backups, first.File)));
        Assert.DoesNotContain(Directory.GetFiles(Backups), f => f.EndsWith(".uploading"));
    }

    [Fact]
    public async Task AnUploadThatIsTooLarge_OrNoBackup_OrDamaged_IsRefused_AndLeavesNothingBehind()
    {
        var backup = await BackupFileAsync();
        var store = Store();
        var damaged = await File.ReadAllBytesAsync(backup);
        damaged[300] ^= 0xff; // inside the inner archive: the checksum catches it
        // A file whose manifest reads well but names another writer.
        var foreign = new MemoryStream();
        using (var zip = new ZipArchive(foreign, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry(BackupManifest.FileName).Open()))
                writer.Write((BackupFile.ReadManifest(backup) with { WrittenBy = "another-tool" }).ToJson());
        foreign.Position = 0;

        Assert.Equal("backup.tooLarge", Key(() => store.SaveUploadAsync(File.OpenRead(backup), "big.zip", 1000, default)));
        Assert.Equal("backup.corrupt", Key(() => store.SaveUploadAsync(new MemoryStream(RandomNumberGenerator.GetBytes(5000)), "noise.zip", long.MaxValue, default)));
        Assert.Equal("backup.corrupt", Key(() => store.SaveUploadAsync(new MemoryStream(damaged), "damaged.zip", long.MaxValue, default)));
        Assert.Equal("backup.corrupt", Key(() => store.SaveUploadAsync(foreign, "foreign.zip", long.MaxValue, default)));
        Assert.Empty(Directory.GetFiles(Backups));
    }

    [Fact]
    public async Task ADownload_IsAFileOfTheFolderByItsBareName_AndNothingElse()
    {
        var backup = await BackupFileAsync();
        Directory.CreateDirectory(Backups);
        File.Copy(backup, Path.Combine(Backups, "a.zip"));
        await File.WriteAllTextAsync(Path.Combine(_data, "secret.txt"), "outside the folder");
        await File.WriteAllTextAsync(Path.Combine(Backups, ".hidden"), "hidden");
        var store = Store();

        await using (var stream = store.OpenRead("a.zip"))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal(await File.ReadAllBytesAsync(backup), copy.ToArray());
        }
        foreach (var name in new[] { "../secret.txt", "..", ".hidden", "", "missing.zip", Path.Combine(_data, "secret.txt") })
            Assert.Equal("backup.notFound", Assert.Throws<NotFoundException>(() => store.OpenRead(name)).Key);
    }
}

public class BackupKeyMakerTests
{
    [Fact]
    public void AMadeUpPassphrase_Is100RandomBits_InFiveGroupsOfLettersAndDigitsThatCannotBeMistaken()
    {
        var phrases = Enumerable.Range(0, 50).Select(_ => BackupKeyMaker.NewPassphrase()).ToList();

        Assert.All(phrases, p => Assert.Matches(new Regex("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){4}$"), p));
        Assert.Equal(phrases.Count, phrases.Distinct().Count());
    }

    [Fact]
    public void AKeySet_OpensWhatItsRecipientEncrypts_AndAShortPassphraseIsRefused()
    {
        var maker = new BackupKeyMaker();

        var made = maker.Make(null);
        var typed = maker.Make("correct horse battery staple");

        Assert.True(made.Generated);
        Assert.False(typed.Generated);
        Assert.Equal("correct horse battery staple", typed.Passphrase);
        foreach (var set in new[] { made, typed })
        {
            Assert.True(BackupKeys.Opens(AgeKeys.ParseIdentity(set.Identity), set.Recipient));
            Assert.Equal(16, Convert.FromBase64String(set.Salt).Length);
            Assert.Equal(AgeKeys.ParseIdentity(set.Identity), BackupKeys.Identity(set.Passphrase, Convert.FromBase64String(set.Salt), Argon2idParameters.Default)); // the passphrase alone derives it again
        }
        Assert.NotEqual(made.Salt, typed.Salt);
        var refused = Assert.Throws<DomainException>(() => maker.Make("short"));
        Assert.Equal(("backup.passphraseTooShort", 12), (refused.Key, refused.Args["min"]));
    }
}

public sealed class BackupFactsTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-facts-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }

    [Fact]
    public async Task TheDatabase_SaysHowLargeItIs_AndTheMigrationItIsAt()
    {
        await using var db = new TestDatabase();
        var facts = db.Get<IDatabaseFacts>();
        await using var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

        Assert.True(await facts.SizeAsync(default) > 4096);
        Assert.Equal(context.Database.GetMigrations().Last(), await facts.LastMigrationAsync(default));
    }

    [Fact]
    public async Task TheFiles_AreCountedPerRoot_ThePhotosOnlyWhenTheyHaveARootOfTheirOwn()
    {
        void Write(string path, int bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
        }
        Write(Path.Combine(_data, "uploads", "users", "a", "1"), 100);
        Write(Path.Combine(_data, "uploads", "vehicles", "b", "picture", "2"), 50);
        Write(Path.Combine(_data, "photos", "vehicles", "b", "refuelings", "c", "3"), 7);

        var apart = await new StorageUsage(Options.Create(new StorageOptions { Path = Path.Combine(_data, "uploads"), PhotosPath = Path.Combine(_data, "photos") })).MeasureAsync(default);
        var together = await new StorageUsage(Options.Create(new StorageOptions { Path = Path.Combine(_data, "uploads") })).MeasureAsync(default);
        var nothing = await new StorageUsage(Options.Create(new StorageOptions { Path = Path.Combine(_data, "none") })).MeasureAsync(default);

        Assert.Equal(new FileUsage(150, 7), apart);
        Assert.Equal(new FileUsage(150, null), together);
        Assert.Equal(new FileUsage(0, null), nothing);
    }
}
