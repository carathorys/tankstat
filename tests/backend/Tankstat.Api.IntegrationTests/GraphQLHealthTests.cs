using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Api.IntegrationTests;

/// <summary>In-process host: real DI, real HotChocolate pipeline, real EF Core (in-memory SQLite).</summary>
public class GraphQLHealthTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string ProviderVar = "Database__Provider";
    private const string ConnectionVar = "Database__ConnectionString";

    private readonly WebApplicationFactory<Program> _factory;

    public GraphQLHealthTests(WebApplicationFactory<Program> factory)
    {
        // Env vars outrank appsettings.json: this is the supported override mechanism.
        Environment.SetEnvironmentVariable(ProviderVar, "Sqlite");
        Environment.SetEnvironmentVariable(ConnectionVar, "Data Source=:memory:");
        _factory = factory;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ProviderVar, null);
        Environment.SetEnvironmentVariable(ConnectionVar, null);
    }

    [Fact]
    public async Task EnvironmentVariables_OverrideAppSettings()
    {
        Environment.SetEnvironmentVariable(ProviderVar, "SqlServer");
        Environment.SetEnvironmentVariable(ConnectionVar, "Server=override");

        using var factory = new WebApplicationFactory<Program>();
        var options = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.Equal(DatabaseProvider.SqlServer, options.Provider);
        Assert.Equal("Server=override", options.ConnectionString);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HealthQuery_ReturnsOk()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/graphql",
            new { query = "{ health { status databaseReachable } }" });

        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("health");
        Assert.Equal("ok", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("databaseReachable").GetBoolean());
    }

    [Fact]
    public async Task UnknownField_ReturnsGraphQLError()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/graphql", new { query = "{ nope }" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0);
    }
}
