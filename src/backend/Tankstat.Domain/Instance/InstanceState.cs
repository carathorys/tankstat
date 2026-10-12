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

    public static InstanceState New() => new() { Generation = Guid.NewGuid() };

    public void RecordStart(string mode, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode.Length > MaxModeLength) throw new DomainException("instance.modeInvalid", "The authentication mode must be named.");
        LastStartMode = mode;
        LastStartedAt = now;
    }

    /// <summary>Gives a row that has no generation yet its first one; false when it had one.</summary>
    public bool EnsureGeneration()
    {
        if (Generation != Guid.Empty) return false;
        Generation = Guid.NewGuid();
        return true;
    }

    /// <summary>A new generation: the data is another history from now on.</summary>
    public void Restored(DateTimeOffset now, DateTimeOffset restorePoint)
    {
        Generation = Guid.NewGuid();
        RestoredAt = now;
        RestorePoint = restorePoint;
    }
}
