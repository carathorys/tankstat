namespace Tankstat.Domain.Access;

public enum ResourceType
{
    Vehicle,
}

/// <summary>What a resource grant lets the grantee do with the resource (more features can follow).</summary>
public enum GrantedFeature
{
    /// <summary>Work with the logs (refuelings, ...) of a vehicle, without being allowed to change the vehicle itself.</summary>
    Logs,
}

/// <summary>
/// Gives <see cref="GranteeId"/> a feature of one particular resource, independent of the owner-wide
/// <see cref="AccessGrant"/>s. The level is Edit (add, change, trash, restore) or Delete (also delete permanently).
/// </summary>
public sealed class ResourceGrant
{
    private ResourceGrant() { } // EF Core

    public Guid Id { get; private set; }
    public ResourceType ResourceType { get; private set; }
    public Guid ResourceId { get; private set; }
    public Guid GranteeId { get; private set; }
    public GrantedFeature Feature { get; private set; }
    public AccessLevel Level { get; private set; }

    public static ResourceGrant Create(ResourceType type, Guid resourceId, Guid granteeId, GrantedFeature feature, AccessLevel level)
    {
        Validate(level);
        return new ResourceGrant { Id = Guid.NewGuid(), ResourceType = type, ResourceId = resourceId, GranteeId = granteeId, Feature = feature, Level = level };
    }

    public void ChangeLevel(AccessLevel level)
    {
        Validate(level);
        Level = level;
    }

    private static void Validate(AccessLevel level)
    {
        if (level is not (AccessLevel.Edit or AccessLevel.Delete))
            throw new DomainException("share.levelRequired", "Choose a level of Edit or Delete.");
    }
}
