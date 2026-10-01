using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations before the host starts accepting requests.</summary>
internal sealed class DatabaseMigrator(IDbContextFactory<AppDbContext> dbFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
