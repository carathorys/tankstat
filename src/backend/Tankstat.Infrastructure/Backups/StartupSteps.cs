using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>The two start-up steps a restore from the command line runs when the server is not running: the migrations, then the restore.</summary>
public static class StartupSteps
{
    public static async Task MigrateAndRestoreAsync(IServiceProvider services, CancellationToken ct)
    {
        var steps = services.GetServices<IHostedService>().ToList();
        await steps.OfType<DatabaseMigrator>().Single().StartAsync(ct);
        await steps.OfType<RestoreStep>().Single().StartAsync(ct);
    }
}
