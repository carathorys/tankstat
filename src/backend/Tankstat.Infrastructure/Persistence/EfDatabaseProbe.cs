using Microsoft.EntityFrameworkCore;
using Tankstat.Application;

namespace Tankstat.Infrastructure.Persistence;

internal sealed class EfDatabaseProbe(IDbContextFactory<AppDbContext> dbFactory) : IDatabaseProbe
{
    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        // Opening the connection (rather than CanConnectAsync) so that a SQLite file that does not
        // exist yet counts as reachable: opening creates it, whereas CanConnect reports it missing.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }
}
