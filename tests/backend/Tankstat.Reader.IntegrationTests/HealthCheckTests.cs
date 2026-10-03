using Tankstat.Reader.Tools;

namespace Tankstat.Reader.IntegrationTests;

public class HealthCheckTests
{
    [Theory]
    [InlineData("http://+:8081", null, "http://localhost:8081/v1/health")]
    [InlineData("http://*:9000;https://*:9443", null, "http://localhost:9000/v1/health")]
    [InlineData("http://0.0.0.0:8081", null, "http://localhost:8081/v1/health")]
    [InlineData(null, "8080", "http://localhost:8080/v1/health")]
    [InlineData(null, null, "http://localhost:8080/v1/health")]
    public void TheUrlComesFromWhereTheServerListens(string? urls, string? ports, string expected) =>
        Assert.Equal(expected, HealthCheckCommand.DefaultUrl(urls, ports));

    [Fact]
    public async Task NothingListening_IsUnhealthy() =>
        Assert.Equal(1, await HealthCheckCommand.RunAsync(["http://127.0.0.1:9/v1/health"]));
}
