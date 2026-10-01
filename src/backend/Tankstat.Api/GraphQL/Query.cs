using Tankstat.Application.Health;

namespace Tankstat.Api.GraphQL;

public sealed class Query
{
    public Task<HealthReport> GetHealth([Service] HealthReporter health, CancellationToken ct) =>
        health.ReportAsync(ct);
}
