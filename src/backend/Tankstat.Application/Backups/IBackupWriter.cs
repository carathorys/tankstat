namespace Tankstat.Application.Backups;

/// <summary>
/// What a backup run is asked for. <paramref name="Output"/> is the file to write (default: a dated name in <c>Backups:Path</c>);
/// <paramref name="Photos"/> whether a photos root of its own goes in (default: <c>Backups:Photos</c>); <paramref name="Encryption"/> the
/// recipient to encrypt for (default: <c>Backups:Encryption</c>, else plain); <paramref name="SafetyNet"/> marks the backup a restore
/// writes of the state it replaces.
/// </summary>
/// <param name="Environment">Over <see cref="BackupOptions.Environment"/> for this one (the command's <c>--environment</c>).</param>
/// <param name="Secrets">Over <see cref="BackupOptions.Secrets"/> for this one (the command's <c>--secrets</c>).</param>
public sealed record BackupRequest(
    string? Output = null, bool? Photos = null, BackupEncryption? Encryption = null, bool SafetyNet = false, BackupEnvironmentMode? Environment = null, bool? Secrets = null);

public sealed record BackupResult(string Path, BackupManifest Manifest);

/// <summary>Writes one backup file: every table, the pictures (and photos), the keys of an encrypted one, and the manifest.</summary>
public interface IBackupWriter
{
    Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct);
}
