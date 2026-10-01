namespace Tankstat.Domain.Access;

/// <summary>Lets <see cref="GranteeId"/> work with all data owned by <see cref="OwnerId"/> at <see cref="Level"/>.</summary>
public sealed class AccessGrant
{
    private AccessGrant() { } // EF Core

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid GranteeId { get; private set; }
    public AccessLevel Level { get; private set; }

    public static AccessGrant Create(Guid ownerId, Guid granteeId, AccessLevel level)
    {
        if (ownerId == granteeId) throw new DomainException("access.selfGrant", "Users always have full access to their own data.");
        if (level == AccessLevel.None) throw new DomainException("access.levelRequired", "Use a grant level of View, Edit or Delete.");
        if (!Enum.IsDefined(level)) throw new DomainException("access.unknownLevel", $"Unknown access level '{level}'.", new { Level = level.ToString() });
        return new AccessGrant { Id = Guid.NewGuid(), OwnerId = ownerId, GranteeId = granteeId, Level = level };
    }

    public void ChangeLevel(AccessLevel level)
    {
        if (level == AccessLevel.None || !Enum.IsDefined(level)) throw new DomainException("access.levelRequired", "Use a grant level of View, Edit or Delete.");
        Level = level;
    }
}
