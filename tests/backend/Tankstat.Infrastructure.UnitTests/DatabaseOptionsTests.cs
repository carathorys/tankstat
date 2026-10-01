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
    public void MissingConnectionString_FailsValidation()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Database:Provider"] = "Sqlite" }));
    }
}
