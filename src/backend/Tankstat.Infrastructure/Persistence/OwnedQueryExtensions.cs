using System.Linq.Expressions;
using Tankstat.Application.Access;
using Tankstat.Domain.Access;

namespace Tankstat.Infrastructure.Persistence;

public static class OwnedQueryExtensions
{
    /// <summary>Carries a list as a property of a closure so EF turns it into a query parameter (and can cache the query).</summary>
    private sealed record ListHolder(List<Guid> Items);

    /// <summary>
    /// Restricts any query over owned entities to what the access layer allows; the one place this filter lives. Rows match
    /// when their owner is in scope, or when <paramref name="resourceId"/> (the vehicle for a vehicle or one of its logs) was
    /// granted individually.
    /// </summary>
    public static IQueryable<T> InScope<T>(this IQueryable<T> query, OwnerScope scope, Expression<Func<T, Guid>> resourceId) where T : IOwned
    {
        if (scope.IsAll) return query;

        var e = Expression.Parameter(typeof(T), "e");
        Expression Contains(List<Guid> items, Expression value) =>
            Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
                Expression.Property(Expression.Constant(new ListHolder(items)), nameof(ListHolder.Items)), value);

        Expression filter = Contains(scope.Owners.ToList(), Expression.Property(e, nameof(IOwned.OwnerId)));
        if (scope.Resources.Count > 0)
            filter = Expression.OrElse(filter, Contains(scope.Resources.ToList(), new ParameterReplacer(resourceId.Parameters[0], e).Visit(resourceId.Body)));

        return query.Where(Expression.Lambda<Func<T, bool>>(filter, e));
    }

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
