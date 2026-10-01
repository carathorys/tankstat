namespace Tankstat.Domain.Access;

/// <summary>What a user may do with someone else's data. Each level includes the ones below it.</summary>
public enum AccessLevel
{
    None = 0,
    View = 1,
    Edit = 2,
}
