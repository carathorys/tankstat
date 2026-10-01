namespace Tankstat.Domain.Access;

/// <summary>
/// What a user may do with data. Each level includes the ones below it. Edit may create, change, move to the trash and
/// restore; only Delete (always held by owners and administrators) may delete permanently.
/// </summary>
public enum AccessLevel
{
    None = 0,
    View = 1,
    Edit = 2,
    Delete = 3,
}
