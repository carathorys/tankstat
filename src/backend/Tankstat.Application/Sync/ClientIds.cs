using Tankstat.Domain;

namespace Tankstat.Application.Sync;

/// <summary>
/// Adds with an id the client chose (<see cref="EntityId"/>) are idempotent: sending the same add again, after an answer that never arrived
/// or when a device replays what it did offline, finds what it created the first time and answers with it, instead of creating it twice.
/// The id of something someone else created is refused.
/// </summary>
public static class ClientIds
{
    /// <param name="sameAdd">The existing entity is the one this add created (same creator, same vehicle).</param>
    public static T Repeat<T>(T existing, bool sameAdd, Guid id) =>
        sameAdd ? existing : throw new DomainException("sync.idTaken", $"The id {id} is already in use.", new { Id = id });
}

/// <summary>
/// An edit, trash or restore may say which <c>Version</c> of the entity it was made from; when the entity was saved since, the change is
/// refused, so it does not silently overwrite what someone else did meanwhile. Checked after the access check, so it never reveals
/// anything about an entity the user may not see.
/// </summary>
public static class VersionCheck
{
    public static void Require(int? expected, int actual)
    {
        if (expected is { } e && e != actual)
            throw new DomainException("sync.versionMismatch", "It was changed meanwhile.", new { Expected = e, Actual = actual });
    }
}
