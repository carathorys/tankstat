using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tankstat.Application;

namespace Tankstat.Infrastructure.Persistence;

internal sealed class EfDatabaseProbe(IDbContextFactory<AppDbContext> dbFactory, ILogger<EfDatabaseProbe> logger) : IDatabaseProbe
{
    // What the log last said: -1 nothing yet, 0 unreachable, 1 reachable. The footer asks for the health every few seconds, so the log
    // only speaks when this changes (the first time a database cannot be reached, and when it is back), never on every check.
    private int _state = -1;

    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        // Opening the connection (rather than CanConnectAsync) so that a SQLite file that does not
        // exist yet counts as reachable: opening creates it, whereas CanConnect reports it missing.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.OpenConnectionAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (Interlocked.Exchange(ref _state, 0) != 0) logger.LogWarning(e, "The database cannot be reached");
            return false;
        }
        if (Interlocked.Exchange(ref _state, 1) == 0) logger.LogInformation("The database is reachable again");
        return true;
    }
}
