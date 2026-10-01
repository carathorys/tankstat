using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application;
using Tankstat.Infrastructure;

namespace Tankstat.Infrastructure.UnitTests;

public class DatabaseProbeTests
{
    private static IDatabaseProbe Probe(string connectionString)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = connectionString,
        }).Build();
        return new ServiceCollection().AddInfrastructure(config).BuildServiceProvider().GetRequiredService<IDatabaseProbe>();
    }

    [Fact]
    public async Task Reachable_WhenDatabaseOpens() =>
        Assert.True(await Probe("Data Source=:memory:").IsReachableAsync(default));

    [Fact]
    public async Task Unreachable_WhenDatabaseCannotOpen() =>
        Assert.False(await Probe("Data Source=/nonexistent-dir/x.db;Mode=ReadOnly").IsReachableAsync(default));
}
