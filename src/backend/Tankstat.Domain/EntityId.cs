namespace Tankstat.Domain;

/// <summary>
/// The id of a new entity. A client may choose it (a UUID made up before saving, so an add sent again after a lost answer, or replayed
/// after being offline, finds what it already created instead of creating it twice); otherwise the server makes one up.
/// </summary>
public static class EntityId
{
    public static Guid OrNew(Guid? id)
    {
        if (id == Guid.Empty) throw new DomainException("id.empty", "An id cannot be empty.");
        return id ?? Guid.NewGuid();
    }
}
