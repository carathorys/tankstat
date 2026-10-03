using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The log is where an operator finds out that a start changed the database, and by which migrations.</summary>
public class DatabaseMigratorTests
{
    [Fact]
    public async Task AFreshDatabase_IsMigrated_AndTheLogNamesWhatWasApplied_ThenTheNextStartSaysNothingWasToDo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tankstat-{Guid.NewGuid():N}.db");
        var log = new CapturedLog();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={path}",
        }).Build();
        await using var services = new ServiceCollection().AddLogging(b => b.AddProvider(log)).AddApplication(config).AddInfrastructure(config).BuildServiceProvider();
        try
        {
            var migrator = services.GetServices<IHostedService>().OfType<DatabaseMigrator>().Single();
            string[] all;
            await using (var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
                all = db.Database.GetMigrations().ToArray();

            await migrator.StartAsync(default);

            // Two lines, and none about the consumption backfill: a new database has no vehicles for it to calculate.
            var first = log.From<DatabaseMigrator>().ToList();
            Assert.Equal(2, first.Count);
            Assert.All(first, e => Assert.Equal(LogLevel.Information, e.Level));
            Assert.Equal(all.Length, first[0].Values["Count"]);
            Assert.All(all, name => Assert.Contains(name, (string)first[0].Values["Migrations"]!)); // every one is named, so a failed upgrade can be found
            Assert.Equal(all.Length, first[1].Values["Count"]);

            log.Clear();
            await migrator.StartAsync(default);

            var again = Assert.Single(log.From<DatabaseMigrator>());
            Assert.Equal(LogLevel.Information, again.Level);
            Assert.False(again.Values.ContainsKey("Migrations")); // nothing was applied
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
