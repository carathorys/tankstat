using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>A migrated, throwaway SQLite file wired through the real <c>AddInfrastructure</c> registrations.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tankstat-{Guid.NewGuid():N}.db");

    public ServiceProvider Services { get; }

    /// <summary>Everything the services of this database logged.</summary>
    public CapturedLog Log { get; } = new();

    /// <param name="services">Extra registrations (a test's EF interceptor, for one) applied before the provider is built.</param>
    public TestDatabase(Dictionary<string, string?>? extraSettings = null, Action<IServiceCollection>? services = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={_path}",
        };
        foreach (var (key, value) in extraSettings ?? []) settings[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection().AddLogging(b => b.AddProvider(Log)).AddApplication(config).AddInfrastructure(config);
        services?.Invoke(collection);
        Services = collection.BuildServiceProvider();

        using var db = Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        db.Database.Migrate();
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        // Only this database's pooled connections: ClearAllPools would also close the ones of tests running in parallel.
        using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
        File.Delete(_path);
    }
}
