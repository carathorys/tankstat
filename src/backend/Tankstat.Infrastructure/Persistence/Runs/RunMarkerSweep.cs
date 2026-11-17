using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Runs;
using Tankstat.Domain.Runs;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// Ends the marks of runs that can no longer be running (#335): their server stopped, the database rolled the run back, and only the
/// mark says it existed. On SQLite, which one server has to itself, every mark open at start is such a one. On a database server
/// another instance may be in the middle of a run, so only a mark older than the limit is ended, at start and once a minute
/// (until then the gate ignores it by its age anyway). Ended marks are kept for a month, for whoever tells the run's starter.
/// </summary>
internal sealed class RunMarkerSweep(
    IRunMarkerRepository markers, RunGateState gate, IOptions<DatabaseOptions> database, IOptionsMonitor<RunOptions> settings, TimeProvider clock,
    ILogger<RunMarkerSweep> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    public override async Task StartAsync(CancellationToken ct)
    {
        await SweepAsync(atStart: true, ct); // before the server takes requests: a mark left open must not make saves wait
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (database.Value.Provider == DatabaseProvider.Sqlite) return;
        using var timer = new PeriodicTimer(Every, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
            {
                try
                {
                    await SweepAsync(atStart: false, stopping);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "The marks of runs that did not finish could not be looked at; trying again later");
                }
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
    }

    internal async Task SweepAsync(bool atStart, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var alone = atStart && database.Value.Provider == DatabaseProvider.Sqlite;
        foreach (var marker in await markers.ListOpenAsync(alone ? null : now - settings.CurrentValue.Limit, ct))
        {
            if (gate.IsOpenHere(marker.Id)) continue; // this process's own run, alive past its limit: it ends its mark itself
            await markers.EndAsync(marker.Id, RunEnd.Interrupted, now, ct);
            logger.LogWarning("Run {RunId} started by {UserId} did not finish: the server stopped while it ran, nothing of it was kept", marker.Id, marker.StartedById);
        }
        if (atStart) await markers.PruneAsync(now - Keep, ct);
        gate.Forget();
    }
}
