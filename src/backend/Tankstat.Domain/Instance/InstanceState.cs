namespace Tankstat.Domain.Instance;

/// <summary>
/// One row about this installation: the authentication mode the app last started in, written at every start, so a start in another mode
/// can sign every device out (a session issued in one mode must not keep working in the next); and which history the data has, whose
/// <see cref="Generation"/> changes whenever a restore replaces the data as a whole (stamped after the rows are loaded, so it never comes
/// from a backup), so a device with offline data notices that the ids and versions it holds belong to another history and starts afresh.
/// </summary>
public sealed class InstanceState
{
    public const int SingletonId = 1;
    public const int MaxModeLength = 20;

    private InstanceState() { }

    public int Id { get; private set; } = SingletonId;

    /// <summary>The authentication mode of the last start, by name; null before the first start that recorded one.</summary>
    public string? LastStartMode { get; private set; }

    public DateTimeOffset? LastStartedAt { get; private set; }

    /// <summary>Which history the data has; <see cref="Guid.Empty"/> only for a row written before generations existed, until the next start stamps one.</summary>
    public Guid Generation { get; private set; }

    /// <summary>When the data was last replaced by a restore, and the backup's time it was taken back to; null for an instance never restored.</summary>
    public DateTimeOffset? RestoredAt { get; private set; }

    public DateTimeOffset? RestorePoint { get; private set; }

    /// <summary>
    /// Since when the instance waits for its setup (its first administrator, or with sign-in off the acknowledgement that everyone may do
    /// everything); kept once the setup is completed. Null with <see cref="SetupCompletedAt"/>: undecided, a row written before setup existed
    /// (or a backup's from then), which the next start decides by whether the database holds data.
    /// </summary>
    public DateTimeOffset? SetupPendingSince { get; private set; }

    /// <summary>When the setup was completed, and how; null while it waits or is undecided. A restore brings the backup's along.</summary>
    public DateTimeOffset? SetupCompletedAt { get; private set; }

    public SetupCompletion? SetupCompletedVia { get; private set; }

    public SetupPhase Setup => SetupCompletedAt is not null ? SetupPhase.Completed : SetupPendingSince is not null ? SetupPhase.Pending : SetupPhase.Undecided;

    public static InstanceState New() => new() { Generation = Guid.NewGuid() };

    public void RecordStart(string mode, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode.Length > MaxModeLength) throw new DomainException("instance.modeInvalid", "The authentication mode must be named.");
        LastStartMode = mode;
        LastStartedAt = now;
    }

    /// <summary>A new generation: the data is another history from now on.</summary>
    public void Restored(DateTimeOffset now, DateTimeOffset restorePoint)
    {
        Generation = Guid.NewGuid();
        RestoredAt = now;
        RestorePoint = restorePoint;
    }
}
