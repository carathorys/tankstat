using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The backups folder (<c>Backups:Path</c>): the restore points in it, uploads into it and downloads out of it, and the restore that waits
/// for the next start. Files by bare name only, so nothing outside the folder is ever reached.
/// </summary>
internal sealed class FileBackupStore(IOptionsMonitor<BackupOptions> options, IDbContextFactory<AppDbContext> dbFactory) : IBackupStore
{
    private const string Uploading = ".uploading";

    /// <summary>
    /// The folder as the app started with it, like the storage roots: only the other backup settings apply at once. A folder that moved on a
    /// reload would split an upload or a run between two, and settings that stopped validating since (a file edited by hand) would make every
    /// read throw, the public notices' look for a waiting restore among them.
    /// </summary>
    private string Root { get; } = options.CurrentValue.Root;

    public string Folder => Root;

    public BackupManifest ReadManifest(string fileName) => BackupFile.ReadManifest(PathOf(fileName));

    public IReadOnlyDictionary<string, string?>? ReadSettings(string fileName, BackupManifest manifest, string? identity) =>
        BackupFile.ReadSettings(PathOf(fileName), manifest, identity is null ? null : AgeKeys.ParseIdentity(identity));

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
        bool opens;
        try
        {
            opens = BackupKeys.Opens(key, encryption.Recipient);
        }
        catch (FormatException)
        {
            throw new DomainException("backup.corrupt", "The file is damaged or is not a Tankstat backup."); // a manifest whose recipient is none
        }
        if (!opens) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        return AgeKeys.FormatIdentity(key);
    }

    public async Task RequireRestorableMigrationAsync(BackupManifest manifest, CancellationToken ct) => await MigrationOfAsync(dbFactory, manifest, ct);

    /// <summary>
    /// This provider's id of the migration the backup was made at, found by name (each provider stamps the same migration with its own
    /// timestamp, and a backup may come from another provider): the restore sets the database up at it (or finds it there), loads the
    /// backup, and the migrations bring it up to date. A migration this release does not know is a newer release's: <c>backup.otherMigration</c>.
    /// </summary>
    internal static async Task<string> MigrationOfAsync(IDbContextFactory<AppDbContext> dbFactory, BackupManifest manifest, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return MigrationModels.IdOf(db, manifest.Migration)
            ?? throw new DomainException("backup.otherMigration", "The backup was made at a migration this release does not know.",
                new { Backup = manifest.Migration, Database = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() });
    }

    public bool RestorePending => File.Exists(RestoreMarker.PathIn(Root));

    public void RequestRestore(RestoreMarker marker)
    {
        // Looked for once more right before the marker is written: a delete may have taken the file since its manifest was read (deriving
        // a key from a passphrase takes a while), and a marker naming no backup would restart the server only to refuse it.
        PathOf(marker.File);
        marker.Write(Root);
    }

    public IReadOnlyList<RestorePoint> List()
    {
        if (!Directory.Exists(Root)) return [];
        var points = new List<RestorePoint>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.zip"))
        {
            var name = Path.GetFileName(path);
            if (!IsBackupName(name)) continue; // a hidden file, which no download or restore would take
            try
            {
                points.Add(new RestorePoint(name, new FileInfo(path).Length, BackupFile.ReadManifest(path)));
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
        var (name, file) = ClaimUpload(fileName);
        var temporary = Path.Combine(Root, name + Uploading);
        var moved = false;
        try
        {
            await using (file)
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
                // On disk before the rename publishes it, as the writer's: after a power cut the final name never holds a file cut short.
                file.Flush(flushToDisk: true);
            }
            var manifest = BackupFile.ReadManifest(temporary);
            await BackupFile.VerifyChecksumAsync(temporary, manifest, ct);
            var path = Path.Combine(Root, name);
            File.Move(temporary, path);
            moved = true;
            return new RestorePoint(name, new FileInfo(path).Length, manifest);
        }
        finally
        {
            if (!moved) File.Delete(temporary); // this upload's own file: it was created new for it, so it is never another's
        }
    }

    /// <summary>
    /// A backup of the folder to download: one <see cref="List"/> would show (a <c>*.zip</c> whose manifest reads), never any other file there,
    /// such as the restore marker with its identity or a file still being written; <c>backup.notFound</c> otherwise.
    /// </summary>
    public Stream OpenRead(string fileName)
    {
        var path = PathOf(fileName);
        try
        {
            BackupFile.ReadManifest(path);
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
        catch (Exception e) when (e is DomainException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw NotFound(); // not a backup, or gone since the look
        }
    }

    public void Delete(string fileName)
    {
        if (!IsBackupName(fileName)) return; // never anything but a backup of the folder (List only hands out such names)
        var path = Path.Combine(Root, fileName);
        if (File.Exists(path)) File.Delete(path); // gone already: nothing to remove
    }

    /// <summary>A backup of the folder by its bare name; <c>backup.notFound</c> for anything else (a path, a hidden file, not a backup's name, one not there).</summary>
    private string PathOf(string fileName)
    {
        if (!IsBackupName(fileName)) throw NotFound();
        var path = Path.Combine(Root, fileName);
        if (!File.Exists(path)) throw NotFound();
        return path;
    }

    /// <summary>
    /// Whether a name can be a backup's: a bare <c>*.zip</c> name, not hidden. The rest of the folder (the restore marker, which holds an
    /// identity in plain, a refused one, a <c>.partial</c> or <c>.uploading</c> file still being written) is never listed, read or sent.
    /// </summary>
    private static bool IsBackupName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && Path.GetFileName(fileName) == fileName && !fileName.StartsWith('.') && fileName.EndsWith(".zip", StringComparison.Ordinal);

    /// <summary>
    /// <c>uploaded-&lt;the name&gt;.zip</c>: only letters, digits, dots, dashes and underscores of the name the browser sent, then a number
    /// when that name is taken, so an upload never replaces a file and is never taken for one the app wrote. The name is claimed by creating
    /// its temporary file new, one claim at a time: two uploads of one name at once never share a file, and each removes only its own.
    /// </summary>
    private (string Name, FileStream File) ClaimUpload(string? fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? "");
        var clean = new StringBuilder();
        foreach (var c in stem) clean.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');
        var safe = clean.ToString().Trim('-', '.');
        if (safe.Length == 0) safe = "backup";
        if (safe.Length > 80) safe = safe[..80];
        lock (Claims)
        {
            for (var n = 1; ; n++)
            {
                var name = $"{RestorePoint.UploadedPrefix}{safe}{(n == 1 ? "" : $"-{n}")}.zip";
                var path = Path.Combine(Root, name);
                // The temporary file first: another upload's rename, outside this lock, makes the final name before it removes its temporary
                // one, so a look in this order never finds neither while that upload holds the name.
                if (File.Exists(path + Uploading) || File.Exists(path)) continue;
                FileStream file;
                try
                {
                    file = new FileStream(path + Uploading, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                }
                catch (IOException) when (File.Exists(path + Uploading) || File.Exists(path))
                {
                    continue; // taken meanwhile by something other than this server's uploads (a copy by hand)
                }
                if (!File.Exists(path)) return (name, file);
                file.Dispose(); // the final name appeared meanwhile, put there by hand: this file is ours, the name is not
                File.Delete(path + Uploading);
            }
        }
    }

    /// <summary>Claims of upload names, one at a time (only this server uploads into the folder, so the process is the scope).</summary>
    private static readonly Lock Claims = new();

    private static DomainException TooLarge(long maxBytes) =>
        new("backup.tooLarge", "The backup is larger than an upload may be; copy it into the backups folder instead.", new { MaxMb = maxBytes / 1024 / 1024 });

    private static NotFoundException NotFound() => new("backup.notFound", "There is no such backup in the backups folder.");
}
