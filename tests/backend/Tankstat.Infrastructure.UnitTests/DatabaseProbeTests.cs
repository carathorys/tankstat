using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application;
using Tankstat.Infrastructure;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class DatabaseProbeTests
{
    private static IDatabaseProbe Probe(string connectionString, CapturedLog? log = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = connectionString,
        }).Build();
        return new ServiceCollection().AddLogging(b => b.AddProvider(log ?? new CapturedLog())).AddInfrastructure(config).BuildServiceProvider().GetRequiredService<IDatabaseProbe>();
    }

    [Fact]
    public async Task Reachable_WhenDatabaseOpens() =>
        Assert.True(await Probe("Data Source=:memory:").IsReachableAsync(default));

    [Fact]
    public async Task Reachable_WhenSqliteFileDoesNotExistYet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tankstat-{Guid.NewGuid():N}.db");
        try
        {
            Assert.True(await Probe($"Data Source={path}").IsReachableAsync(default));
        }
        finally
        {
            // Only this database's pooled connections: ClearAllPools would also close the ones of tests running in parallel.
            using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Unreachable_WhenDatabaseCannotOpen() =>
        Assert.False(await Probe("Data Source=/nonexistent-dir/x.db;Mode=ReadOnly").IsReachableAsync(default));

    [Fact]
    public async Task ADatabaseThatIsDownAndComesBack_IsLoggedOnce_EachWay_NotOnEveryHealthCheck()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tankstat-{Guid.NewGuid():N}");
        var log = new CapturedLog();
        var probe = Probe($"Data Source={Path.Combine(folder, "x.db")}", log); // the folder is missing: the file cannot be created
        var ours = () => log.From<Persistence.EfDatabaseProbe>().ToList(); // EF Core logs its own error on every failed attempt
        try
        {
            Assert.False(await probe.IsReachableAsync(default));
            Assert.False(await probe.IsReachableAsync(default)); // the footer asks every few seconds
            Assert.False(await probe.IsReachableAsync(default));
            var warning = Assert.Single(ours());
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.NotNull(warning.Exception); // why, for whoever has to fix it

            Directory.CreateDirectory(folder);

            Assert.True(await probe.IsReachableAsync(default));
            Assert.True(await probe.IsReachableAsync(default));
            Assert.Equal([LogLevel.Warning, LogLevel.Information], ours().Select(e => e.Level));
        }
        finally
        {
            // Only this database's pooled connections: ClearAllPools would also close the ones of tests running in parallel.
            using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(folder, "x.db")}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ADatabaseThatNeverWentDown_SaysNothing()
    {
        var log = new CapturedLog();

        Assert.True(await Probe("Data Source=:memory:", log).IsReachableAsync(default));

        Assert.Empty(log.Entries);
    }
}
