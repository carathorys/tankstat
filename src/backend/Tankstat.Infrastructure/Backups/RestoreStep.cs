using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// At start, after the database migrations and before anything else touches the data: gives a fresh instance its generation and tells
/// <see cref="InstanceState"/> which generation the data has, for every session answer.
/// </summary>
internal sealed class RestoreStep(IDbContextFactory<AppDbContext> dbFactory, InstanceState state, ILogger<RestoreStep> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var instance = await db.Instance.SingleOrDefaultAsync(ct);
        if (instance is null)
        {
            instance = Instance.Fresh();
            db.Instance.Add(instance);
            await db.SaveChangesAsync(ct);
            logger.LogDebug("The instance got its generation {Generation}", instance.Generation);
        }
        state.Set(new InstanceInfo(instance.Generation, instance.RestorePoint));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
