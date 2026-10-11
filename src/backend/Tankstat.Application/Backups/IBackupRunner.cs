namespace Tankstat.Application.Backups;

/// <summary>How the last run went: when it started and ended, the file it wrote, or the error key that stopped it.</summary>
public sealed record BackupRun(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? File, long? Bytes, string? Error);

/// <summary>Whether a backup is being written now, and the last run of this process's life.</summary>
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
