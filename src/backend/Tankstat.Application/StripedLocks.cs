namespace Tankstat.Application;

/// <summary>
/// A lock per key (a vehicle, a log, a user, an address) within this process, striped over a fixed set of semaphores so they never pile up: two
/// keys may share one, which only makes one wait for the other. Other processes on the same database are not held back by it.
/// </summary>
internal sealed class StripedLocks(int stripes = 64)
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public SemaphoreSlim For(Guid key) => gates[(uint)key.GetHashCode() % gates.Length];

    /// <summary>
    /// The lock of a key that is text (a provider and a subject, an address); the same text always gets the same one within this process.
    /// Case is ignored: SQL Server and MySQL compare such text by the column's collation, which is case-insensitive by default, so
    /// <c>Alice</c> and <c>alice</c> are one user there and must wait for each other.
    /// </summary>
    public SemaphoreSlim For(string key) => gates[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(key) % gates.Length];
}
