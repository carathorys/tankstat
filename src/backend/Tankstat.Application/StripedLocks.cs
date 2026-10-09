namespace Tankstat.Application;

/// <summary>
/// A lock per key (a vehicle, a log, a user) within this process, striped over a fixed set of semaphores so they never pile up: two
/// keys may share one, which only makes one wait for the other. Other processes on the same database are not held back by it.
/// </summary>
internal sealed class StripedLocks(int stripes = 64)
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public SemaphoreSlim For(Guid key) => gates[(uint)key.GetHashCode() % gates.Length];
}
