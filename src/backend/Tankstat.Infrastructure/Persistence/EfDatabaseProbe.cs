using Microsoft.EntityFrameworkCore;
using Tankstat.Application;

namespace Tankstat.Infrastructure.Persistence;

internal sealed class EfDatabaseProbe(IDbContextFactory<AppDbContext> dbFactory) : IDatabaseProbe
{
    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct);
    }
}
