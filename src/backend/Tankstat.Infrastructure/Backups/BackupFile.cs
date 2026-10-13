using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Tankstat.Application.Backups;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Reads a backup file as <see cref="BackupWriter"/> lays it out: the manifest by name (never by position), the inner archive's checksum,
/// and the inner archive itself, decrypted with the identity when the backup is encrypted. Nothing here touches the database.
/// </summary>
public static class BackupFile
{
    /// <summary>
    /// The manifest of a backup file; <c>backup.corrupt</c> when the file is not a backup. Only a whole manifest Tankstat wrote counts: another
    /// program's <c>manifest.json</c> (a browser extension's, say) reads as one with nothing set.
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
        if (manifest is not { Archive: { Name: not null, Sha256: not null }, Contents.Tables: not null, Sizes: not null } || !Usable(manifest.Encryption))
            throw NotABackup();
        return manifest;
    }

    /// <summary>
    /// An encryption a manifest may name: complete, with a salt an identity can be derived with and Argon2id costs within reason, since they
    /// are used before any key is tried.
    /// </summary>
    private static bool Usable(BackupEncryption? encryption) =>
        encryption is null
        || encryption is { Recipient: not null, Argon2id: { MemoryKiB: >= 8 and <= 4 * 1024 * 1024, Passes: >= 1 and <= 100, Lanes: >= 1 and <= 64 } }
           && BackupEncryption.IsSalt(encryption.Salt);

    /// <summary>Checks the inner archive against the manifest's checksum; <c>backup.corrupt</c> when it does not match.</summary>
    public static void VerifyChecksum(string path, BackupManifest manifest)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            using var data = (zip.GetEntry(manifest.Archive.Name) ?? throw NotABackup()).Open();
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), manifest.Archive.Sha256, StringComparison.OrdinalIgnoreCase)) throw NotABackup();
        }
        catch (InvalidDataException)
        {
            throw NotABackup(); // a damaged entry header, although the central directory the manifest was found by read fine
        }
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
