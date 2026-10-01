namespace Tankstat.Domain.Access;

/// <summary>Administrator-controlled, instance-wide access options (a single row).</summary>
public sealed class AccessSettings
{
    public const int SingletonId = 1;

    private AccessSettings() { } // EF Core

    public int Id { get; private set; } = SingletonId;

    /// <summary>What every user may do with data owned by other users, unless a grant gives more.</summary>
    public AccessLevel DefaultLevelForOthers { get; private set; } = AccessLevel.None;

    public static AccessSettings Default() => new();

    public void SetDefaultLevelForOthers(AccessLevel level)
    {
        if (!Enum.IsDefined(level)) throw new DomainException($"Unknown access level '{level}'.");
        DefaultLevelForOthers = level;
    }
}
