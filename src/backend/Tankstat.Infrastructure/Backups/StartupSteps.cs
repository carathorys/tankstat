using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>The two start-up steps a restore from the command line runs when the server is not running: the migrations, then the restore.</summary>
public static class StartupSteps
{
    /// <summary>Runs both; what the restore step did with the restore that waited.</summary>
    public static async Task<RestoreOutcome> MigrateAndRestoreAsync(IServiceProvider services, CancellationToken ct)
    {
        var steps = services.GetServices<IHostedService>().ToList();
        await steps.OfType<DatabaseMigrator>().Single().StartAsync(ct);
        var restore = steps.OfType<RestoreStep>().Single();
        await restore.StartAsync(ct);
        return restore.Outcome;
    }
}
