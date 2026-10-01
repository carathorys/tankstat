namespace Tankstat.Application.Health;

public sealed record HealthReport(string Status, string Version, TimeSpan Uptime, bool DatabaseReachable);
