using System.Reflection;

namespace Tankstat.Application.Health;

public sealed class HealthReporter(TimeProvider clock, IDatabaseProbe database)
{
    private static readonly string AppVersion = VersionOf(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Assembly.GetExecutingAssembly().GetName().Version);

    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();

    public async Task<HealthReport> ReportAsync(CancellationToken ct)
    {
        var reachable = await database.IsReachableAsync(ct);

        return new HealthReport(
            reachable ? "ok" : "degraded",
            AppVersion,
            clock.GetUtcNow() - _startedAt,
            reachable);
    }

    /// <summary>
    /// The version the app was released as (<c>-p:Version</c>, e.g. "0.12.0-preview"), without the build metadata the SDK appends
    /// ("+&lt;commit&gt;"). The assembly version cannot carry a pre-release label, so it only stands in when there is nothing better.
    /// </summary>
    public static string VersionOf(string? informational, Version? assembly)
    {
        var version = informational?.Split('+', 2)[0].Trim();
        return string.IsNullOrEmpty(version) ? assembly?.ToString() ?? "0.0.0" : version;
    }
}
