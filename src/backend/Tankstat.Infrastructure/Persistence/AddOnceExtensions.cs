using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;

namespace Tankstat.Infrastructure.Persistence;

internal static class AddOnceExtensions
{
    /// <summary>
    /// Inserts a new entity whose id a client may have chosen. When the same add arrives twice at once, both find nothing and both insert;
    /// the second one's primary key violation is turned into <c>false</c> (the row is there, saved by the other), so the service answers it
    /// like any repeated add instead of failing with an unkeyed error. Any other failure is rethrown as it was.
    /// </summary>
    public static Task<bool> AddOnceAsync<T>(this IDbContextFactory<AppDbContext> factory, T entity, Guid id, CancellationToken ct) where T : class =>
        factory.AddOnceAsync(entity, e => EF.Property<Guid>(e, "Id") == id, ct);

    /// <summary>
    /// The same for an entity another unique key identifies (a user's provider and subject): <paramref name="isThere"/> finds the row the
    /// other insert saved, with the equality the unique index uses, so a clash is told from any other failure.
    /// </summary>
    public static async Task<bool> AddOnceAsync<T>(this IDbContextFactory<AppDbContext> factory, T entity, Expression<Func<T, bool>> isThere, CancellationToken ct) where T : class
    {
        ExceptionDispatchInfo failure;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Set<T>().Add(entity);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException e)
            {
                failure = ExceptionDispatchInfo.Capture(e); // looked into with a fresh context: this one still holds the failed insert
            }
        }
        await using var check = await factory.CreateDbContextAsync(ct);
        if (await check.Set<T>().IgnoreQueryFilters().AnyAsync(isThere, ct)) return false;
        failure.Throw();
        return false; // not reached
    }
}
