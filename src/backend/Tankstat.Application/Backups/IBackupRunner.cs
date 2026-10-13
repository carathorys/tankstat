namespace Tankstat.Application.Backups;

/// <summary>A run: when it started and, once it ended, when, the file it wrote, or the error key that stopped it (all null while it goes on).</summary>
public sealed record BackupRun(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? File, long? Bytes, string? Error);

/// <summary>Whether a backup is being written now, and that run (<see cref="Running"/>), else the last one of this host's life.</summary>
public sealed record BackupRunStatus(bool Running, BackupRun? Last);

/// <summary>
/// Writes backups one at a time, in the background: a run asked for while one is under way is not started twice (the caller hears that
/// one is running). What a run did stays in <see cref="Status"/> for the administration page.
/// </summary>
public interface IBackupRunner
{
    BackupRunStatus Status { get; }

    /// <summary>Starts a run unless one is under way; true when this call started it. It returns at once: the run goes on by itself.</summary>
    bool Start();
}
