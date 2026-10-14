using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Reads a backup file as <see cref="BackupWriter"/> lays it out: the manifest by name (never by position), the inner archive's checksum,
/// and the inner archive itself, decrypted with the identity when the backup is encrypted. Nothing here touches the database.
/// </summary>
public static class BackupFile
{
    /// <summary>
    /// The manifest of a backup file; <c>backup.corrupt</c> when the file is not a backup. Only a whole manifest Tankstat wrote counts: another
    /// program's <c>manifest.json</c> (a browser extension's, say) reads as one with nothing set, and one that lacks what every backup states
    /// (the release and the database it came from, where its photos are) is no backup either, so nothing that lists backups meets a gap.
    /// </summary>
    public static BackupManifest ReadManifest(string path)
    {
        BackupManifest manifest;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(BackupManifest.FileName) ?? throw NotABackup();
            using var reader = new StreamReader(entry.Open());
            manifest = BackupManifest.FromJson(reader.ReadToEnd());
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            throw NotABackup();
        }
        if (manifest.WrittenBy != BackupManifest.Tankstat) throw NotABackup();
        if (manifest.Format > BackupManifest.CurrentFormat)
            throw new DomainException("backup.newerFormat", "The backup was written by a newer release.", new { manifest.Format, manifest.AppVersion });
        if (manifest is not { AppVersion: not null, Provider: not null, Archive: { Name: not null, Sha256: not null }, Contents.Tables: not null, Sizes: not null }
            || !Enum.IsDefined(manifest.Contents.Photos) || !Usable(manifest.Encryption) || !Usable(manifest.Settings, manifest.Encryption is not null))
            throw NotABackup();
        return manifest;
    }

    /// <summary>
    /// A settings member a manifest may carry: none (an older backup), or a known state, and when the file went in, the entry an encrypted or
    /// plain backup writes it as, with its checksum.
    /// </summary>
    private static bool Usable(BackupSettingsFile? settings, bool encrypted) =>
        settings is null
        || Enum.IsDefined(settings.State) && (settings.State != BackupSettingsState.Included
            || settings.Sha256 is not null && settings.Entry == (encrypted ? BackupSettingsFile.Encrypted : BackupSettingsFile.Plain));

    /// <summary>
    /// The settings file a backup carries, by key (null when it carries none: an older backup, or none went in): its entry checked against the
    /// manifest's checksum, then read, decrypted with the identity in an encrypted backup (checked against the recipient first,
    /// <c>backup.wrongKey</c>). Damage is <c>backup.corrupt</c>. Only the small entry is read, never the data.
    /// </summary>
    public static IReadOnlyDictionary<string, string?>? ReadSettings(string path, BackupManifest manifest, byte[]? identity)
    {
        if (manifest.Settings is not { State: BackupSettingsState.Included } settings) return null;
        if (manifest.Encryption is { } encryption)
        {
            if (identity is null) throw new DomainException("backup.wrongKey", "The backup is encrypted; the identity or the passphrase is needed.");
            if (!BackupKeys.Opens(identity, encryption.Recipient)) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        }
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(settings.Entry!) ?? throw NotABackup();
            // A settings file is a few kilobytes: an entry far larger is no backup's, and is never read into memory whole.
            if (entry.Length > MaxSettingsBytes) throw NotABackup();
            using var stored = new MemoryStream();
            using (var data = entry.Open()) CopyAtMost(data, stored, MaxSettingsBytes);
            RequireSettingsChecksum(SHA256.HashData(stored.ToArray()), settings);
            stored.Position = 0;
            using var plain = manifest.Encryption is null ? stored : AgeStream.Decrypt(stored, identity!, leaveOpen: true);
            using var json = new MemoryStream();
            plain.CopyTo(json);
            json.Position = 0;
            return SettingsFileStore.Read(json);
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or JsonException or CryptographicException)
        {
            throw NotABackup();
        }
    }

    /// <summary>The most a backup's settings entry may hold, as stored.</summary>
    private const int MaxSettingsBytes = 1024 * 1024;

    /// <summary>Copies <paramref name="source"/>, refusing it as no backup once it holds more than <paramref name="limit"/> bytes (whatever its header said).</summary>
    private static void CopyAtMost(Stream source, Stream target, int limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            total += read;
            if (total > limit) throw NotABackup();
            target.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// An encryption a manifest may name: complete, with a salt an identity can be derived with and Argon2id costs within reason, since they
    /// are used before any key is tried.
    /// </summary>
    private static bool Usable(BackupEncryption? encryption) =>
        encryption is null
        || encryption is { Recipient: not null, Argon2id: { MemoryKiB: >= 8 and <= 4 * 1024 * 1024, Passes: >= 1 and <= 100, Lanes: >= 1 and <= 64 } }
           && BackupEncryption.IsSalt(encryption.Salt);

    /// <summary>Checks the inner archive (and the settings file, when it went in) against the manifest's checksums; <c>backup.corrupt</c> when one does not match.</summary>
    public static void VerifyChecksum(string path, BackupManifest manifest)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            using (var data = (zip.GetEntry(manifest.Archive.Name) ?? throw NotABackup()).Open())
                RequireChecksum(SHA256.HashData(data), manifest);
            if (manifest.Settings is { State: BackupSettingsState.Included } settings)
            {
                using var stored = (zip.GetEntry(settings.Entry!) ?? throw NotABackup()).Open();
                RequireSettingsChecksum(SHA256.HashData(stored), settings);
            }
        }
        catch (InvalidDataException)
        {
            throw NotABackup(); // a damaged entry header, although the central directory the manifest was found by read fine
        }
    }

    /// <summary><see cref="VerifyChecksum"/> for a request (an upload of a gigabyte): no thread held while it is hashed, and given up with <paramref name="ct"/>.</summary>
    public static async Task VerifyChecksumAsync(string path, BackupManifest manifest, CancellationToken ct)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            await using (var data = (zip.GetEntry(manifest.Archive.Name) ?? throw NotABackup()).Open())
                RequireChecksum(await SHA256.HashDataAsync(data, ct), manifest);
            if (manifest.Settings is { State: BackupSettingsState.Included } settings)
            {
                await using var stored = (zip.GetEntry(settings.Entry!) ?? throw NotABackup()).Open();
                RequireSettingsChecksum(await SHA256.HashDataAsync(stored, ct), settings);
            }
        }
        catch (InvalidDataException)
        {
            throw NotABackup();
        }
    }

    private static void RequireChecksum(byte[] sha256, BackupManifest manifest)
    {
        if (!string.Equals(Convert.ToHexStringLower(sha256), manifest.Archive.Sha256, StringComparison.OrdinalIgnoreCase)) throw NotABackup();
    }

    private static void RequireSettingsChecksum(byte[] sha256, BackupSettingsFile settings)
    {
        if (!string.Equals(Convert.ToHexStringLower(sha256), settings.Sha256, StringComparison.OrdinalIgnoreCase)) throw NotABackup();
    }

    /// <summary>The identity of a passphrase for this backup, derived as the manifest says.</summary>
    public static byte[] IdentityOf(BackupManifest manifest, string passphrase)
    {
        var encryption = manifest.Encryption ?? throw new DomainException("backup.notEncrypted", "The backup is not encrypted; no passphrase opens it.");
        return BackupKeys.Identity(passphrase, Convert.FromBase64String(encryption.Salt), encryption.Argon2id);
    }

    /// <summary>
    /// Unpacks the inner archive (tables, files, keys) into a folder. An encrypted backup needs its identity, checked against the manifest's
    /// recipient before anything is read (<c>backup.wrongKey</c>); a plain one takes none. The folder must be empty; when it is created here,
    /// it is the owner's only (Unix), since what lands in it is the instance's data in plain, the Data Protection keys too, which the app
    /// itself keeps owner-only.
    /// </summary>
    public static void Extract(string path, BackupManifest manifest, byte[]? identity, string folder)
    {
        if (manifest.Encryption is { } encryption)
        {
            if (identity is null) throw new DomainException("backup.wrongKey", "The backup is encrypted; the identity or the passphrase is needed.");
            if (!BackupKeys.Opens(identity, encryption.Recipient)) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        }
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(folder);
        else Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException($"The folder is not empty: {folder}");
        var inner = Path.Combine(folder, BackupArchive.Plain);
        try
        {
            using (var zip = ZipFile.OpenRead(path))
            using (var data = (zip.GetEntry(manifest.Archive.Name) ?? throw NotABackup()).Open())
            using (var plain = manifest.Encryption is null ? data : AgeStream.Decrypt(data, identity!, leaveOpen: true))
            using (var output = File.Create(inner))
                plain.CopyTo(output);
            ZipFile.ExtractToDirectory(inner, folder);
        }
        catch (InvalidDataException)
        {
            throw NotABackup();
        }
        finally
        {
            File.Delete(inner); // the inner archive in plain, also when it was cut short (a damaged chunk, a full disk)
        }
    }

    private static DomainException NotABackup() => new("backup.corrupt", "The file is damaged or is not a Tankstat backup.");
}
