namespace Tankstat.Application.Backups;

/// <summary>
/// A run: when it started and, once it ended, when, the file it wrote, or the error key that stopped it with the values its text is filled
/// with (all null while it goes on).
/// </summary>
public sealed record BackupRun(
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? File, long? Bytes, string? Error, IReadOnlyDictionary<string, object?>? ErrorArgs = null);

/// <summary>Whether a backup is being written now, and that run (<see cref="Running"/>), else the last one of this host's life.</summary>
public sealed record BackupRunStatus(bool Running, BackupRun? Last);

/// <summary>
/// Writes backups one at a time, in the background: a run asked for while one is under way is not started twice (the caller hears that
/// one is running). What a run did stays in <see cref="Status"/> for the administration page. After a backup is written, retention
/// removes what it says (<see cref="BackupRetention"/>); a failed run notifies the administrators (<see cref="BackupFailureNotifier"/>).
/// </summary>
public interface IBackupRunner
{
    BackupRunStatus Status { get; }

    /// <summary>
    /// Starts a run unless one is under way; true when this call started it. It returns at once: the run goes on by itself.
    /// <paramref name="actorId"/>: the administrator who asked (left out of the failure notice), null for the schedule.
    /// </summary>
    bool Start(Guid? actorId = null);
}
