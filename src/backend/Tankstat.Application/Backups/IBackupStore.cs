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
    /// Refuses, before anything is written or restarted, a backup the start would refuse anyway: one made at another migration than the
    /// database is at (<c>backup.otherMigration</c>, compared by name as the start compares them).
    /// </summary>
    Task RequireSameMigrationAsync(BackupManifest manifest, CancellationToken ct);

    /// <summary>Whether a restore waits for the next start.</summary>
    bool RestorePending { get; }

    /// <summary>Asks for a restore at the next start (one already waiting is replaced).</summary>
    void RequestRestore(RestoreMarker marker);
}
