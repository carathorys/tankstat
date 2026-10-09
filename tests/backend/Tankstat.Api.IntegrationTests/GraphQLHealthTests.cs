using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// In-process host: real DI, HotChocolate pipeline, EF Core and migrations on a throwaway SQLite file.
/// The database is configured through environment variables, the same mechanism that overrides appsettings.json.
/// </summary>
public sealed class ApiFixture : IDisposable
{
    private const string ProviderVar = "Database__Provider";
    private const string ConnectionVar = "Database__ConnectionString";
    private const string ModeVar = "Auth__Mode";
    private const string StorageVar = "Storage__Path";
    private const string PhotosVar = "Storage__PhotosPath";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}.db");

    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-uploads");

    public string PhotosPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-photos");

    public WebApplicationFactory<Program> Factory { get; }

    public ApiFixture()
    {
        Environment.SetEnvironmentVariable(ProviderVar, "Sqlite");
        Environment.SetEnvironmentVariable(ConnectionVar, $"Data Source={_path}");
        Environment.SetEnvironmentVariable(ModeVar, "None"); // these tests are about no-auth; appsettings.json may say otherwise
        Environment.SetEnvironmentVariable(StorageVar, UploadsPath);
        Environment.SetEnvironmentVariable(PhotosVar, PhotosPath);
        Factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        Factory.Dispose();
        Environment.SetEnvironmentVariable(ProviderVar, null);
        Environment.SetEnvironmentVariable(ConnectionVar, null);
        Environment.SetEnvironmentVariable(ModeVar, null);
        Environment.SetEnvironmentVariable(StorageVar, null);
        Environment.SetEnvironmentVariable(PhotosVar, null);
        foreach (var folder in new[] { UploadsPath, PhotosPath })
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        // Only this database's pooled connections: ClearAllPools would also close the ones of tests running in parallel.
        using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
        File.Delete(_path);
    }
}

/// <summary>One shared host/database for all API integration tests (env vars are process-global).</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}

[Collection(ApiCollection.Name)]
public class GraphQLHealthTests(ApiFixture api)
{
    [Fact]
    public void EnvironmentVariables_OverrideAppSettings()
    {
        var options = api.Factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.Equal(DatabaseProvider.Sqlite, options.Provider);
        Assert.StartsWith("Data Source=", options.ConnectionString);
        Assert.Contains("tankstat-it-", options.ConnectionString);
    }

    [Fact]
    public async Task HealthQuery_ReturnsOk()
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql",
            new { query = "{ health { status databaseReachable } }" });

        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("health");
        Assert.Equal("ok", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("databaseReachable").GetBoolean());
    }

    [Fact]
    public async Task UnknownField_ReturnsGraphQLError()
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql", new { query = "{ nope }" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0);
    }
}
