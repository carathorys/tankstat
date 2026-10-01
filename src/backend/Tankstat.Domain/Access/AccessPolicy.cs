namespace Tankstat.Domain.Access;

/// <summary>The single place that decides how much access a user has to data owned by someone else.</summary>
public static class AccessPolicy
{
    /// <summary>Administrators and owners have full access; everyone else gets the larger of the default and any matching grant.</summary>
    public static AccessLevel Resolve(
        Guid userId, bool isAdmin, Guid ownerId, AccessLevel defaultForOthers, IEnumerable<AccessGrant> grants)
    {
        if (isAdmin || userId == ownerId) return AccessLevel.Edit;

        var level = defaultForOthers;
        foreach (var g in grants)
            if (g.OwnerId == ownerId && g.GranteeId == userId && g.Level > level) level = g.Level;
        return level;
    }
}
