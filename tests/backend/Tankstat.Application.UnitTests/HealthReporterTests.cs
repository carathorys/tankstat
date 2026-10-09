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

    [Theory]
    [InlineData("0.12.0-preview+529e1e5b0af6837639fda8eabadcc3a059a0220d", "0.12.0-preview")] // a pre-release tag keeps its label
    [InlineData("0.11.0+abc", "0.11.0")]
    [InlineData("0.11.0", "0.11.0")]
    [InlineData(null, "0.11.0.0")] // no informational version: the assembly's
    [InlineData("+abc", "0.11.0.0")]
    public void Version_IsTheReleasedOne_WithoutBuildMetadata(string? informational, string expected)
    {
        Assert.Equal(expected, HealthReporter.VersionOf(informational, new Version(0, 11, 0, 0)));
    }

    [Fact]
    public void Version_WithNothingToGoBy_IsZero() => Assert.Equal("0.0.0", HealthReporter.VersionOf(null, null));

    [Fact]
    public async Task Report_UptimeTracksClock()
    {
        var clock = new FakeTimeProvider();
        var reporter = new HealthReporter(clock, new FakeProbe(true));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), (await reporter.ReportAsync(default)).Uptime);
    }
}
