using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The start-up steps a restore from the command line runs when the server is not running, as a start would: the restore and then the
/// migrations (<see cref="DatabaseMigrator"/>, which applies a waiting restore first), then the generation (<see cref="RestoreStep"/>).
/// </summary>
public static class StartupSteps
{
    /// <summary>Runs them; what the restore step did with the restore that waited.</summary>
    public static async Task<RestoreOutcome> RestoreAndMigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        var steps = services.GetServices<IHostedService>().ToList();
        await steps.OfType<DatabaseMigrator>().Single().StartAsync(ct);
        var restore = steps.OfType<RestoreStep>().Single();
        await restore.StartAsync(ct);
        return restore.Outcome;
    }
}
