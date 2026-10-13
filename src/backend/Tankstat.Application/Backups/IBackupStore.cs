namespace Tankstat.Application.Backups;

/// <summary>The backups folder as the application sees it: the files in it by name, their keys, and the restore that waits for the next start.</summary>
public interface IBackupStore
{
    /// <summary>The manifest of a backup in the folder; <c>backup.notFound</c> for a name that is not a file there (a path is never one).</summary>
    BackupManifest ReadManifest(string fileName);

    /// <summary>
    /// The identity that opens an encrypted backup, from the passphrase (derived as the manifest says) or as given; checked against the
    /// manifest's recipient (<c>backup.wrongKey</c>). <c>backup.keyRequired</c> when neither came; null for a backup that is not encrypted.
    /// </summary>
    string? IdentityFor(BackupManifest manifest, string? passphrase, string? identity);

    /// <summary>
    /// Refuses, before anything is written or restarted, a backup the start would refuse anyway: one made at a migration this release does
    /// not know, a newer release's (<c>backup.otherMigration</c>, compared by name as the start compares them). One made at an older migration
    /// is restored and brought up to date.
    /// </summary>
    Task RequireRestorableMigrationAsync(BackupManifest manifest, CancellationToken ct);

    /// <summary>Whether a restore waits for the next start.</summary>
    bool RestorePending { get; }

    /// <summary>
    /// Asks for a restore at the next start (one already waiting is replaced); <c>backup.notFound</c> when the backup it names is no longer in
    /// the folder (deleted since its manifest was read), so no restart is asked for a restore the start would refuse.
    /// </summary>
    void RequestRestore(RestoreMarker marker);

    /// <summary>The backups in the folder, newest first: every file whose manifest can be read (one copied in by hand too); anything else is left out.</summary>
    IReadOnlyList<RestorePoint> List();

    /// <summary>The folder, in full, so an administrator knows where to copy a file in or out.</summary>
    string Folder { get; }

    /// <summary>
    /// Stores an uploaded backup under a name of its own (<c>uploaded-&lt;its name&gt;</c>, so retention never takes it for one the app
    /// wrote), streamed to a temporary file first: larger than <paramref name="maxBytes"/> is <c>backup.tooLarge</c>, a file that is no
    /// backup (manifest or checksum) is <c>backup.corrupt</c>, and neither leaves anything behind. Returns the stored backup.
    /// </summary>
    Task<RestorePoint> SaveUploadAsync(Stream content, string? fileName, long maxBytes, CancellationToken ct);

    /// <summary>A backup of the folder by its bare file name, to download; <c>backup.notFound</c> otherwise.</summary>
    Stream OpenRead(string fileName);

    /// <summary>Removes a backup of the folder by its bare file name (retention); one already gone is nothing to do.</summary>
    void Delete(string fileName);
}

/// <summary>A backup in the folder: its file name, its size on disk and what its manifest says.</summary>
public sealed record RestorePoint(string File, long Bytes, BackupManifest Manifest)
{
    /// <summary>The prefix of a backup that came in by upload: never pruned, since the app did not write it into its folder.</summary>
    public const string UploadedPrefix = "uploaded-";

    public bool Uploaded => File.StartsWith(UploadedPrefix, StringComparison.Ordinal);
}
