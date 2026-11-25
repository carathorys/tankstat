namespace Tankstat.Application.Access;

/// <summary>
/// Where <see cref="AccessService"/> keeps what it read of the access tables for the rest of a request (#320): the instance's access
/// settings, the user's grants and their vehicle log grants. HotChocolate gives every query field a service scope of its own, so without it
/// each field that asks a question of access (the list, its counts, every loader of a card) read the same three again. The Api keeps them
/// with the request beside its principal; everywhere else (a command, a background worker, the Seeder) nothing is kept and each ask reads.
/// </summary>
public interface IAccessReads
{
    /// <summary>
    /// The value under <paramref name="key"/> for this request: read once by the first asker, every other asker meanwhile awaiting that one
    /// read. Keyed by what it depends on (the user's id for a grant), so signing in or out in a mutation never hands one user another's.
    /// </summary>
    Task<T> GetAsync<T>(AccessReadKey key, Func<CancellationToken, Task<T>> read, CancellationToken ct);

    /// <summary>Forgets every value: a write to the access tables, so a later field of the same request reads afresh.</summary>
    void Clear();
}

/// <summary>Which read, and for whom (<see cref="Guid.Empty"/> for the instance's settings).</summary>
public readonly record struct AccessReadKey(AccessRead Read, Guid UserId);

public enum AccessRead { Settings, Grants, LogGrants }

/// <summary>Keeps nothing: every ask reads (the default outside a request).</summary>
public sealed class UncachedAccessReads : IAccessReads
{
    public Task<T> GetAsync<T>(AccessReadKey key, Func<CancellationToken, Task<T>> read, CancellationToken ct) => read(ct);

    public void Clear() { }
}
