using System.Text;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The backups folder (<c>Backups:Path</c>): the restore points in it, uploads into it and downloads out of it, and the restore that waits
/// for the next start. Files by bare name only, so nothing outside the folder is ever reached.
/// </summary>
internal sealed class FileBackupStore(IOptions<BackupOptions> options) : IBackupStore
{
    private const string Uploading = ".uploading";

    private string Root => options.Value.Root;

    public string Folder => Root;

    public BackupManifest ReadManifest(string fileName) => BackupFile.ReadManifest(PathOf(fileName));

    public string? IdentityFor(BackupManifest manifest, string? passphrase, string? identity)
    {
        if (manifest.Encryption is not { } encryption) return null;
        byte[] key;
        if (!string.IsNullOrEmpty(identity))
        {
            try
            {
                key = AgeKeys.ParseIdentity(identity);
            }
            catch (FormatException)
            {
                throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
            }
        }
        else if (!string.IsNullOrEmpty(passphrase)) key = BackupFile.IdentityOf(manifest, passphrase);
        else throw new DomainException("backup.keyRequired", "The backup is encrypted: its passphrase or identity is needed.");
        if (!BackupKeys.Opens(key, encryption.Recipient)) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        return AgeKeys.FormatIdentity(key);
    }

    public bool RestorePending => File.Exists(RestoreMarker.PathIn(Root));

    public void RequestRestore(RestoreMarker marker) => marker.Write(Root);

    public IReadOnlyList<RestorePoint> List()
    {
        if (!Directory.Exists(Root)) return [];
        var points = new List<RestorePoint>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.zip"))
        {
            try
            {
                points.Add(new RestorePoint(Path.GetFileName(path), new FileInfo(path).Length, BackupFile.ReadManifest(path)));
            }
            catch (Exception e) when (e is DomainException or IOException or UnauthorizedAccessException)
            {
                // Not a backup (or not readable): it is not a restore point.
            }
        }
        return [.. points.OrderByDescending(p => p.Manifest.CreatedAt).ThenBy(p => p.File, StringComparer.Ordinal)];
    }

    public async Task<RestorePoint> SaveUploadAsync(Stream content, string? fileName, long maxBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var name = UploadName(fileName);
        var temporary = Path.Combine(Root, name + Uploading);
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes) throw TooLarge(maxBytes);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            var manifest = BackupFile.ReadManifest(temporary);
            if (manifest.WrittenBy != BackupManifest.Tankstat) throw new DomainException("backup.corrupt", "The file is damaged or is not a Tankstat backup.");
            BackupFile.VerifyChecksum(temporary, manifest);
            var path = Path.Combine(Root, name);
            File.Move(temporary, path);
            return new RestorePoint(name, new FileInfo(path).Length, manifest);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Stream OpenRead(string fileName) => new FileStream(PathOf(fileName), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    /// <summary>A backup of the folder by its bare name; <c>backup.notFound</c> for anything else (a path, a hidden file, one not there).</summary>
    private string PathOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName.StartsWith('.')) throw NotFound();
        var path = Path.Combine(Root, fileName);
        if (!File.Exists(path)) throw NotFound();
        return path;
    }

    /// <summary>
    /// <c>uploaded-&lt;the name&gt;.zip</c>: only letters, digits, dots, dashes and underscores of the name the browser sent, then a number
    /// when that name is taken, so an upload never replaces a file and is never taken for one the app wrote.
    /// </summary>
    private string UploadName(string? fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? "");
        var clean = new StringBuilder();
        foreach (var c in stem) clean.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        var safe = clean.ToString().Trim('-', '.');
        if (safe.Length == 0) safe = "backup";
        if (safe.Length > 80) safe = safe[..80];
        for (var n = 1; ; n++)
        {
            var name = $"{RestorePoint.UploadedPrefix}{safe}{(n == 1 ? "" : $"-{n}")}.zip";
            if (!File.Exists(Path.Combine(Root, name)) && !File.Exists(Path.Combine(Root, name + Uploading))) return name;
        }
    }

    private static DomainException TooLarge(long maxBytes) =>
        new("backup.tooLarge", "The backup is larger than an upload may be; copy it into the backups folder instead.", new { MaxMb = maxBytes / 1024 / 1024 });

    private static NotFoundException NotFound() => new("backup.notFound", "There is no such backup in the backups folder.");
}
