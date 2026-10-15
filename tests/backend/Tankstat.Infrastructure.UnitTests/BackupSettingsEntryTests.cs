using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// The settings file in a backup: an outer entry of its own beside the data, plain or encrypted like it, its checksum in the manifest, read
/// back by key without the data; a backup without one says why, and damage is found out.
/// </summary>
public sealed class BackupSettingsEntryTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-settings-{Guid.NewGuid():N}");
    private string Backups => Path.Combine(_data, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }

    /// <summary>The copy the port hands the writer, as the Api's would after its rules (<see cref="SettingsInBackups"/>).</summary>
    private sealed class GivenCopy(SettingsCopy copy) : ISettingsBackup
    {
        public Task<SettingsCopy> CopyAsync(bool carriesSecrets, BackupEnvironmentMode mode, CancellationToken ct) => Task.FromResult(copy);
        public Task<IReadOnlyList<string>> MissingAsync(BackupEnvironment recorded, IReadOnlyDictionary<string, string?>? restored, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<string> Overridden(IReadOnlyDictionary<string, string?> fromBackup) => [];
        public Task<RestoredSettings> RestoredAsync(IReadOnlyDictionary<string, string?> fromBackup, bool carriesSecrets, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string?>> LeavesAsync(IReadOnlyDictionary<string, string?> fromBackup, bool carriesSecrets, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("tankstat-salt-01");
    private static readonly byte[] Identity = BackupKeys.Identity("correct horse battery staple", Salt, new Argon2idParameters(256, 1, 1));

    private TestDatabase Database(SettingsCopy copy, bool encrypted = false) => new(new Dictionary<string, string?>
    {
        ["Storage:Path"] = Path.Combine(_data, "uploads"),
        ["DataProtection:KeysPath"] = Path.Combine(_data, "keys"),
        ["Backups:Path"] = Backups,
        ["Backups:Encryption:Recipient"] = encrypted ? AgeKeys.FormatRecipient(BackupKeys.Recipient(Identity)) : null,
        ["Backups:Encryption:Salt"] = encrypted ? Convert.ToBase64String(Salt) : null,
    }, services => services.AddSingleton<ISettingsBackup>(new GivenCopy(copy)));

    private static SettingsCopy Copy(bool withSecret) => new(BackupSettingsState.Included,
        new Dictionary<string, string?> { ["Auth:Mode"] = "Standalone", ["Defaults:Currency"] = "zzzz-currency", ["Smtp:Password"] = withSecret ? "zzzz-password" : null },
        withSecret ? ["Storage:Path"] : ["Smtp:Password", "Storage:Path"]);

    [Fact]
    public async Task APlainBackup_CarriesTheFileAsAnEntryOfItsOwn_WithItsChecksum_AndTheManifestNamesWhatStayedOut_NeverAValue()
    {
        await using var db = Database(Copy(withSecret: false));

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        var settings = result.Manifest.Settings!;
        Assert.Equal((BackupSettingsState.Included, BackupSettingsFile.Plain), (settings.State, settings.Entry));
        Assert.Equal(["Smtp:Password", "Storage:Path"], settings.Omitted);
        using (var zip = ZipFile.OpenRead(result.Path))
        {
            Assert.Equal([BackupArchive.Plain, BackupSettingsFile.Plain, BackupManifest.FileName], zip.Entries.Select(e => e.FullName));
            using var entry = zip.GetEntry(BackupSettingsFile.Plain)!.Open();
            var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            Assert.Equal(settings.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
            Assert.Equal(settings.Bytes, bytes.Length);
        }
        BackupFile.VerifyChecksum(result.Path, BackupFile.ReadManifest(result.Path));
        var read = BackupFile.ReadSettings(result.Path, result.Manifest, identity: null)!;
        Assert.Equal(new Dictionary<string, string?> { ["Auth:Mode"] = "Standalone", ["Defaults:Currency"] = "zzzz-currency" }, read.OrderBy(p => p.Key).ToDictionary());
        Assert.DoesNotContain("zzzz", BackupSeed.Manifest(result.Path)); // the manifest is plain: names, never values
    }

    [Fact]
    public async Task AnEncryptedBackup_EncryptsTheFileForTheSameRecipient_WhichOnlyTheIdentityOpens()
    {
        await using var db = Database(Copy(withSecret: true), encrypted: true);

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        Assert.Equal(BackupSettingsFile.Encrypted, result.Manifest.Settings!.Entry);
        using (var zip = ZipFile.OpenRead(result.Path))
        {
            Assert.Equal([BackupArchive.Encrypted, BackupSettingsFile.Encrypted, BackupManifest.FileName], zip.Entries.Select(e => e.FullName));
            var head = new byte[22];
            using var entry = zip.GetEntry(BackupSettingsFile.Encrypted)!.Open();
            entry.ReadExactly(head);
            Assert.Equal("age-encryption.org/v1\n", Encoding.ASCII.GetString(head)); // age opens it: age -d -i
        }
        Assert.Equal("zzzz-password", BackupFile.ReadSettings(result.Path, result.Manifest, Identity)!["Smtp:Password"]);
        Assert.Equal("backup.wrongKey", Assert.Throws<DomainException>(() => BackupFile.ReadSettings(result.Path, result.Manifest, identity: null)).Key);
        var other = BackupKeys.Identity("wrong", Salt, new Argon2idParameters(256, 1, 1));
        Assert.Equal("backup.wrongKey", Assert.Throws<DomainException>(() => BackupFile.ReadSettings(result.Path, result.Manifest, other)).Key);
        Assert.False(db.Log.Mentions("zzzz-password"));
    }

    [Theory]
    [InlineData(BackupSettingsState.None)]
    [InlineData(BackupSettingsState.Ignored)]
    public async Task WithoutAFileToCarry_TheManifestSaysWhy_AndNoEntryIsWritten(BackupSettingsState state)
    {
        await using var db = Database(SettingsCopy.None with { State = state });

        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        Assert.Equal(new BackupSettingsFile(state), result.Manifest.Settings);
        using (var zip = ZipFile.OpenRead(result.Path)) Assert.Equal([BackupArchive.Plain, BackupManifest.FileName], zip.Entries.Select(e => e.FullName));
        Assert.Null(BackupFile.ReadSettings(result.Path, result.Manifest, null));
    }

    [Fact]
    public async Task ADamagedSettingsEntry_IsFoundOut_ByTheChecksumAndByTheRead()
    {
        await using var db = Database(Copy(withSecret: false));
        var result = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var changed = result.Manifest with { Settings = result.Manifest.Settings! with { Sha256 = new string('0', 64) } };

        Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => BackupFile.VerifyChecksum(result.Path, changed)).Key);
        Assert.Equal("backup.corrupt", (await Assert.ThrowsAsync<DomainException>(() => BackupFile.VerifyChecksumAsync(result.Path, changed, default))).Key);
        Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => BackupFile.ReadSettings(result.Path, changed, null)).Key);
    }

    [Fact]
    public void AManifestThatSaysTheFileWentIn_WithoutItsEntryOrChecksum_IsNoBackup()
    {
        Directory.CreateDirectory(_data);
        var file = Path.Combine(_data, "odd.zip");
        var manifest = new BackupManifest(
            BackupManifest.CurrentFormat, BackupManifest.Tankstat, "1.0.0", DateTimeOffset.UnixEpoch, "Sqlite", null, false,
            new BackupContents(new Dictionary<string, long>(), true, BackupPhotos.WithPictures, false), new BackupSizes(0, 0, 0, 0, 0), new BackupArchive(BackupArchive.Plain, "ab"), null,
            new BackupSettingsFile(BackupSettingsState.Included, BackupSettingsFile.Encrypted, "cd")); // a plain backup names the encrypted entry
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry(BackupManifest.FileName).Open()))
            writer.Write(manifest.ToJson());

        Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => BackupFile.ReadManifest(file)).Key);
    }
}
