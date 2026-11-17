namespace Tankstat.Application.Imports;

/// <summary>
/// The files people have open to import cars from (#336): one per person, on the server that took it, for 30 minutes from its last use.
/// A file waits in a scratch folder of its session, with what is unpacked from it; the session's end (its time, a new file of the same
/// person, the account's deletion, a refusal, a restart) deletes the folder. Nothing here saves to the app's database.
/// </summary>
public interface IBackupImportSessions
{
    /// <summary>
    /// Takes a person's upload: streamed into a new session's folder, never more than the limit (<c>import.tooLarge</c>), then looked at.
    /// The person's earlier file goes first. A plain file starts opening at once; an encrypted one waits for its passphrase.
    /// </summary>
    Task<BackupImportStatus> UploadAsync(Guid userId, Stream content, CancellationToken ct);

    /// <summary>The same for a file that is on the server already (a restore point, for an administrator): never copied, never deleted.</summary>
    Task<BackupImportStatus> OpenFileAsync(Guid userId, string path, CancellationToken ct);

    /// <summary>
    /// Tries a key on a file that waits for one: a passphrase, or for a restore point an identity. One try at a time; a wrong one costs an
    /// upload one of its three tries, and the last one the file.
    /// </summary>
    BackupImportStatus SubmitKey(Guid userId, string token, string? passphrase, string? identity);

    /// <summary>The person's session by its token; null when there is none (ended and forgotten, expired, someone else's).</summary>
    BackupImportStatus? Find(Guid userId, string token);

    /// <summary>The opened file's cars; null unless the session is the person's and opened.</summary>
    IBackupCars? Cars(Guid userId, string token);

    /// <summary>Keeps a session from expiring or being replaced while a run reads it; dispose to let go.</summary>
    IDisposable? Hold(Guid userId, string token);

    /// <summary>Ends a session and deletes its folder.</summary>
    void End(Guid userId, string token);

    /// <summary>Ends everything the person had open (the account is deleted).</summary>
    void EndForUser(Guid userId);
}
