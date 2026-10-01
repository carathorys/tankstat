using Microsoft.Extensions.Time.Testing;
using Tankstat.Application;
using Tankstat.Application.Health;

namespace Tankstat.Application.UnitTests;

public class HealthReporterTests
{
    private sealed class FakeProbe(bool reachable) : IDatabaseProbe
    {
        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(reachable);
    }

    [Fact]
    public async Task Report_IsOk_WhenDatabaseReachable()
    {
        var report = await new HealthReporter(new FakeTimeProvider(), new FakeProbe(true)).ReportAsync(default);

        Assert.Equal("ok", report.Status);
        Assert.True(report.DatabaseReachable);
    }

    [Fact]
    public async Task Report_IsDegraded_WhenDatabaseUnreachable()
    {
        var report = await new HealthReporter(new FakeTimeProvider(), new FakeProbe(false)).ReportAsync(default);

        Assert.Equal("degraded", report.Status);
        Assert.False(report.DatabaseReachable);
    }

    [Fact]
    public async Task Report_UptimeTracksClock()
    {
        var clock = new FakeTimeProvider();
        var reporter = new HealthReporter(clock, new FakeProbe(true));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), (await reporter.ReportAsync(default)).Uptime);
    }
}
