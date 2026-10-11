using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Reading a backup file the way the commands and the restore do: the manifest by name, the checksum, the key, the unpacking, and what each refusal says.</summary>
public sealed class BackupFileTests : IDisposable
{
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("tankstat-salt-01");
    private static readonly Argon2idParameters Quick = new(256, 1, 1);
    private static readonly byte[] Identity = BackupKeys.Identity("hunter2 hunter2", Salt, Quick);
    private static readonly BackupEncryption Encryption = new(BackupEncryption.AgeX25519, AgeKeys.FormatRecipient(BackupKeys.Recipient(Identity)), Convert.ToBase64String(Salt), Quick);

    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-file-{Guid.NewGuid():N}");
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
        foreach (var folder in _folders.Where(Directory.Exists)) Directory.Delete(folder, recursive: true);
    }

    private async Task<BackupResult> Backup(BackupEncryption? encryption = null)
    {
        await using var db = new TestDatabase(new Dictionary<string, string?>
        {
            ["Storage:Path"] = Path.Combine(_data, "uploads"),
            ["DataProtection:KeysPath"] = Path.Combine(_data, "keys"),
            ["Backups:Path"] = Path.Combine(_data, "backups"),
        });
        await BackupSeed.SeedAsync(db, Path.Combine(_data, "uploads"), Path.Combine(_data, "uploads"), Path.Combine(_data, "keys"));
        return await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(Encryption: encryption, Output: Path.Combine(_data, "out", $"{Guid.NewGuid():N}.zip")), default);
    }

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tankstat-extract-{Guid.NewGuid():N}");
        _folders.Add(folder);
        return folder;
    }

    private static void RewriteManifest(string file, Func<string, string> change)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Update);
        var entry = zip.GetEntry(BackupManifest.FileName)!;
        string json;
        using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(BackupManifest.FileName).Open());
        writer.Write(change(json));
    }

    private static string Key(Action action) => Assert.Throws<DomainException>(action).Key;

    [Fact]
    public async Task ABackupFromANewerRelease_IsRefused_ByItsFormat()
    {
        var backup = await Backup();
        RewriteManifest(backup.Path, json => json.Replace("\"format\": 1", "\"format\": 2"));

        var refused = Assert.Throws<DomainException>(() => BackupFile.ReadManifest(backup.Path));

        Assert.Equal("backup.newerFormat", refused.Key);
        Assert.Equal(2, refused.Args["format"]);
    }

    [Fact]
    public async Task AFileThatIsNotABackup_IsCorrupt_Whether_ItIsNoZip_HasNoManifest_OrAManifestThatIsNotOne()
    {
        var noZip = Path.Combine(_data, "notes.txt");
        Directory.CreateDirectory(_data);
        await File.WriteAllTextAsync(noZip, "not a zip");
        var noManifest = Path.Combine(_data, "empty.zip");
        using (var zip = ZipFile.Open(noManifest, ZipArchiveMode.Create)) zip.CreateEntry("hello.txt");
        var badManifest = (await Backup()).Path;
        RewriteManifest(badManifest, _ => "{ \"format\": ");
        var nullManifest = (await Backup()).Path;
        RewriteManifest(nullManifest, _ => "null");

        foreach (var file in new[] { noZip, noManifest, badManifest, nullManifest }) Assert.Equal("backup.corrupt", Key(() => BackupFile.ReadManifest(file)));
    }

    [Fact]
    public async Task APassphrase_ForABackupThatIsNotEncrypted_HasNothingToOpen()
    {
        var plain = (await Backup()).Manifest;

        Assert.Equal("backup.notEncrypted", Key(() => BackupFile.IdentityOf(plain, "hunter2 hunter2")));
    }

    [Fact]
    public async Task APlainBackup_IsUnpacked_NeedsNoKey_AndTheFolderMustBeEmpty()
    {
        var backup = await Backup();
        var folder = NewFolder();

        BackupFile.Extract(backup.Path, backup.Manifest, null, folder);

        Assert.True(File.Exists(Path.Combine(folder, "tables", "Vehicles.jsonl")));
        Assert.False(File.Exists(Path.Combine(folder, BackupArchive.Plain))); // the inner archive does not stay behind
        Assert.Throws<IOException>(() => BackupFile.Extract(backup.Path, backup.Manifest, null, folder)); // not into a folder that holds something
    }

    [Fact]
    public async Task AnEncryptedBackup_NeedsTheIdentityItWasMadeFor_BeforeAnythingIsUnpacked()
    {
        var backup = await Backup(Encryption);
        var other = BackupKeys.Identity("another passphrase", Salt, Quick);
        var folder = NewFolder();

        Assert.Equal("backup.wrongKey", Key(() => BackupFile.Extract(backup.Path, backup.Manifest, null, folder)));
        Assert.Equal("backup.wrongKey", Key(() => BackupFile.Extract(backup.Path, backup.Manifest, other, folder)));
        Assert.False(Directory.Exists(folder)); // refused before the folder was even made

        BackupFile.Extract(backup.Path, backup.Manifest, BackupFile.IdentityOf(backup.Manifest, "hunter2 hunter2"), folder); // the passphrase derives the identity from the manifest
        Assert.True(File.Exists(Path.Combine(folder, "tables", "Vehicles.jsonl")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "keys")));
    }

    [Fact]
    public async Task AChecksumThatDoesNotMatch_AndAnInnerArchiveThatIsNoZip_AreCorrupt()
    {
        var backup = await Backup();
        BackupFile.VerifyChecksum(backup.Path, backup.Manifest); // fine as written

        RewriteManifest(backup.Path, json => json.Replace(backup.Manifest.Archive.Sha256, new string('0', 64)));
        var changed = BackupFile.ReadManifest(backup.Path);
        Assert.Equal("backup.corrupt", Key(() => BackupFile.VerifyChecksum(backup.Path, changed)));

        // An inner archive that is garbage but whose checksum the manifest states correctly: found out when it is unpacked.
        var garbage = RandomNumberGenerator.GetBytes(5000);
        var file = Path.Combine(_data, "garbage.zip");
        var manifest = backup.Manifest with { Archive = new BackupArchive(BackupArchive.Plain, Convert.ToHexStringLower(SHA256.HashData(garbage))) };
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry(BackupArchive.Plain, CompressionLevel.NoCompression).Open()) entry.Write(garbage);
            using (var entry = new StreamWriter(zip.CreateEntry(BackupManifest.FileName).Open())) entry.Write(manifest.ToJson());
        }
        var read = BackupFile.ReadManifest(file);
        BackupFile.VerifyChecksum(file, read);
        Assert.Equal("backup.corrupt", Key(() => BackupFile.Extract(file, read, null, NewFolder())));

        // A manifest naming an inner archive the file does not hold.
        var missing = Path.Combine(_data, "missing.zip");
        using (var zip = ZipFile.Open(missing, ZipArchiveMode.Create))
            using (var entry = new StreamWriter(zip.CreateEntry(BackupManifest.FileName).Open())) entry.Write(manifest.ToJson());
        Assert.Equal("backup.corrupt", Key(() => BackupFile.VerifyChecksum(missing, BackupFile.ReadManifest(missing))));
        Assert.Equal("backup.corrupt", Key(() => BackupFile.Extract(missing, BackupFile.ReadManifest(missing), null, NewFolder())));
    }
}

/// <summary>The streams of a backup are forward-only: the zip writer and the age writer never seek, which is what lets a backup stream to disk without a copy.</summary>
public class ForwardOnlyStreamTests
{
    private static readonly byte[] Recipient = AgeKeys.ParseRecipient(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "age", "recipient.txt")).Trim());

    [Fact]
    public void TheHashingStream_CountsAndHashesWhatPassesThrough_AndOnlyWrites()
    {
        var inner = new MemoryStream();
        var hashing = new HashingStream(inner, leaveOpen: true);

        hashing.Write([1, 2, 3], 0, 3);
        hashing.Write(new byte[] { 4, 5 });
        hashing.Flush();

        Assert.Equal(5, hashing.Written);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([1, 2, 3, 4, 5])), hashing.Sha256Hex);
        Assert.Equal((false, false, true), (hashing.CanRead, hashing.CanSeek, hashing.CanWrite));
        Assert.Throws<NotSupportedException>(() => hashing.Length);
        Assert.Throws<NotSupportedException>(() => hashing.Position);
        Assert.Throws<NotSupportedException>(() => hashing.Position = 0);
        Assert.Throws<NotSupportedException>(() => hashing.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => hashing.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => hashing.SetLength(0));
        hashing.Dispose();
        Assert.False(hashing.CanWrite);
        Assert.Equal(5, inner.ToArray().Length); // left open for its owner
    }

    [Fact]
    public async Task TheAgeWriterAndReader_AreForwardOnly_TheWriterOnlyWrites_TheReaderOnlyReads()
    {
        var file = new MemoryStream();
        var writer = AgeStream.Encrypt(file, Recipient, leaveOpen: true);
        await writer.WriteAsync(new byte[] { 1, 2, 3 }); // the async path seals nothing until the chunk is full or the stream ends
        writer.Flush();
        Assert.Equal((false, false, true), (writer.CanRead, writer.CanSeek, writer.CanWrite));
        Assert.Throws<NotSupportedException>(() => writer.Length);
        Assert.Throws<NotSupportedException>(() => writer.Position);
        Assert.Throws<NotSupportedException>(() => writer.Position = 0);
        Assert.Throws<NotSupportedException>(() => writer.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => writer.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => writer.SetLength(0));
        writer.Dispose();
        writer.Dispose(); // twice is fine
        Assert.False(writer.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => writer.Write(new byte[1], 0, 1));

        var identity = AgeKeys.ParseIdentity(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "contracts", "age", "identity.txt")).First(l => !l.StartsWith('#')));
        file.Position = 0;
        var reader = AgeStream.Decrypt(file, identity, leaveOpen: true);
        Assert.Equal((true, false, false), (reader.CanRead, reader.CanSeek, reader.CanWrite));
        Assert.Throws<NotSupportedException>(() => reader.Length);
        Assert.Throws<NotSupportedException>(() => reader.Position);
        Assert.Throws<NotSupportedException>(() => reader.Position = 0);
        Assert.Throws<NotSupportedException>(() => reader.Write(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => reader.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => reader.SetLength(0));
        reader.Flush();
        var buffer = new byte[10];
        Assert.Equal(3, await reader.ReadAsync(buffer)); // the default async read goes through the reading one
        Assert.Equal([1, 2, 3], buffer[..3]);
        Assert.Equal(0, reader.Read(buffer, 0, 10)); // the end
        reader.Dispose();
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Read(buffer, 0, 1));
    }

    [Fact]
    public void AFileCutShortInsideTheNonce_OrWithNoPayloadChunkAtAll_IsCorrupt()
    {
        var identity = AgeKeys.ParseIdentity(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "contracts", "age", "identity.txt")).First(l => !l.StartsWith('#')));
        var whole = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "contracts", "age", "plain.txt.age"));
        var head = Encoding.ASCII.GetString(whole, 0, 200);
        var headerEnd = head.IndexOf('\n', head.IndexOf("\n--- ", StringComparison.Ordinal) + 5) + 1;

        Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => AgeStream.Decrypt(new MemoryStream(whole[..(headerEnd + 7)]), identity)).Key); // inside the 16-byte nonce
        using var noPayload = AgeStream.Decrypt(new MemoryStream(whole[..(headerEnd + AgeFormat.PayloadNonceLength)]), identity);
        Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => noPayload.ReadByte()).Key); // the nonce, then nothing: not even a final chunk
    }
}
