using System.Reflection;

namespace Tankstat.Application.Health;

public sealed class HealthReporter(TimeProvider clock, IDatabaseProbe database)
{
    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();

    public async Task<HealthReport> ReportAsync(CancellationToken ct)
    {
        var reachable = await database.IsReachableAsync(ct);

        return new HealthReport(
            reachable ? "ok" : "degraded",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            clock.GetUtcNow() - _startedAt,
            reachable);
    }
}
