namespace Tankstat.Domain.Backups;

/// <summary>
/// The one row that says which history this instance's data has: <see cref="Generation"/> changes whenever a restore replaces the data as a
/// whole (and never comes from a backup, since it is stamped after the rows are loaded), so a device with offline data notices that the ids
/// and versions it holds belong to another history and starts afresh. A fresh instance gets its generation at first start.
/// </summary>
public sealed class Instance
{
    /// <summary>The only row.</summary>
    public const int SingleId = 1;

    public int Id { get; private set; } = SingleId;
    public Guid Generation { get; private set; }
    /// <summary>When the data was last replaced by a restore, and the backup's time it was taken back to; null for an instance never restored.</summary>
    public DateTimeOffset? RestoredAt { get; private set; }
    public DateTimeOffset? RestorePoint { get; private set; }

    private Instance() { }

    public static Instance Fresh() => new() { Generation = Guid.NewGuid() };

    /// <summary>A new generation: the data is another history from now on.</summary>
    public void Restored(DateTimeOffset now, DateTimeOffset restorePoint)
    {
        Generation = Guid.NewGuid();
        RestoredAt = now;
        RestorePoint = restorePoint;
    }
}
