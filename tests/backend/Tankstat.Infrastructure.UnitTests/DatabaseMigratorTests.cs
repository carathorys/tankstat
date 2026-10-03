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
    public async Task AFreshDatabase_IsMigrated_AndTheLogNamesWhatWasApplied_ThenTheNextStartSaysItIsUpToDate()
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
            int all;
            await using (var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
                all = db.Database.GetMigrations().Count();

            await migrator.StartAsync(default);

            var ours = () => log.Entries.Where(e => e.Category == typeof(DatabaseMigrator).FullName).ToList(); // EF Core logs its own lines too (SQL, locks)
            var applying = Assert.Single(ours(), e => e.Message.StartsWith("Applying", StringComparison.Ordinal));
            Assert.Equal(LogLevel.Information, applying.Level);
            Assert.Contains($"Applying {all} database migrations", applying.Message);
            Assert.Contains("_AddRefuelingConsumption", applying.Message); // the names, so a failed upgrade can be found
            Assert.Contains(ours(), e => e.Message.StartsWith($"Applied {all} database migrations", StringComparison.Ordinal));

            log.Clear();
            await migrator.StartAsync(default);

            var again = Assert.Single(ours());
            Assert.Equal(LogLevel.Information, again.Level);
            Assert.Contains("up to date", again.Message);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
