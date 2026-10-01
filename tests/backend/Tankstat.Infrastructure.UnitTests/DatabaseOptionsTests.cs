using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Infrastructure;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

public class DatabaseOptionsTests
{
    private static DatabaseOptions Resolve(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var sp = new ServiceCollection().AddInfrastructure(config).BuildServiceProvider();
        return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    }

    [Fact]
    public void Binds_ProviderAndConnectionString()
    {
        var options = Resolve(new() { ["Database:Provider"] = "PostgreSql", ["Database:ConnectionString"] = "Host=x" });

        Assert.Equal(DatabaseProvider.PostgreSql, options.Provider);
        Assert.Equal("Host=x", options.ConnectionString);
    }

    [Fact]
    public void NothingConfigured_FallsBackToLocalSqlite()
    {
        var options = Resolve([]);

        Assert.Equal(DatabaseProvider.Sqlite, options.Provider);
        Assert.Equal(DatabaseOptions.DefaultSqliteConnectionString, options.ConnectionString);
    }

    [Fact]
    public void Sqlite_WithoutConnectionString_UsesDefault() =>
        Assert.Equal(DatabaseOptions.DefaultSqliteConnectionString,
            Resolve(new() { ["Database:Provider"] = "Sqlite" }).ConnectionString);

    [Fact]
    public void ExplicitConnectionString_IsNotReplacedByDefault() =>
        Assert.Equal("Data Source=other.db",
            Resolve(new() { ["Database:ConnectionString"] = "Data Source=other.db" }).ConnectionString);

    [Fact]
    public void NonSqliteProvider_WithoutConnectionString_FailsValidation() =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Database:Provider"] = "PostgreSql" }));
}
