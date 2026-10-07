namespace Tankstat.Domain.Sync;

/// <summary>
/// What is left of an entity removed for good (an emptied trash, a deleted schedule, a purged vehicle), so a device that keeps a copy
/// drops it at its next download. A vehicle's tombstone stands for everything below it. Trash is not removal: a trashed row is still sent,
/// with its <c>DeletedAt</c>. Kept for <c>Sync:TombstoneRetentionDays</c>; a device that last downloaded before that starts afresh.
/// </summary>
public sealed class Tombstone
{
    private Tombstone() { } // EF Core

    /// <summary>The tombstone's own id: the same entity id can be removed twice (added again by a device that replays an old change).</summary>
    public Guid Id { get; private set; }

    /// <summary>The id of what was removed.</summary>
    public Guid EntityId { get; private set; }

    public OfflineEntityType EntityType { get; private set; }

    /// <summary>The vehicle it belonged to (no foreign key: the vehicle may be gone too).</summary>
    public Guid VehicleId { get; private set; }

    public DateTimeOffset PurgedAt { get; private set; }

    public static Tombstone For(ISynced removed, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), EntityId = removed.Id, EntityType = removed.SyncType, VehicleId = removed.SyncVehicleId, PurgedAt = now };
}
