namespace Tankstat.Application.Backups;

/// <summary>What <c>Session.instance</c> tells every device: which history the data belongs to, and the time it was last taken back to.</summary>
public sealed record InstanceInfo(Guid Generation, DateTimeOffset? RestorePoint);

/// <summary>
/// The instance's generation, read once at start (the row only changes at a start, by a restore) and answered with every session. Until
/// the start-up step set it, an empty generation: a device adopts the first generation it sees without dropping anything.
/// </summary>
public sealed class InstanceState
{
    public InstanceInfo Current { get; private set; } = new(Guid.Empty, null);

    public void Set(InstanceInfo info) => Current = info;
}
