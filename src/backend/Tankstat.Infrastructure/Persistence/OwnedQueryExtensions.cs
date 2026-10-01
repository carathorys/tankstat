using Tankstat.Application.Access;
using Tankstat.Domain.Access;

namespace Tankstat.Infrastructure.Persistence;

public static class OwnedQueryExtensions
{
    /// <summary>Restricts any query over owned entities to what the access layer allows; the one place this filter lives.</summary>
    public static IQueryable<T> InScope<T>(this IQueryable<T> query, OwnerScope scope) where T : IOwned
    {
        if (scope.IsAll) return query;
        var owners = scope.Owners.ToList();
        return query.Where(e => owners.Contains(e.OwnerId));
    }
}
