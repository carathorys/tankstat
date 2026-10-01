using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// A host with its own settings and throwaway SQLite database. Settings are passed in memory (they outrank
/// appsettings.json and environment variables), so several hosts with different auth modes can be tested.
/// </summary>
internal sealed class TestApp : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}.db");

    public WebApplicationFactory<Program> Factory { get; }

    public TestApp(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={_db}",
        };
        foreach (var (key, value) in settings) all[key] = value;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development"); // introspection etc.; never uses the dev appsettings of a real database
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(all));
            b.ConfigureServices(s => services?.Invoke(s));
        });
    }

    public static TestApp Standalone(Dictionary<string, string?>? extra = null) => new(new Dictionary<string, string?>
    {
        ["Auth:Mode"] = "Standalone",
        ["Auth:Standalone:AdminEmail"] = "root@example.com",
        ["Auth:Standalone:AdminPassword"] = "initial-password-1",
    }.Concat(extra ?? []).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>A client with its own cookie jar, i.e. its own browser session.</summary>
    public HttpClient NewClient() => Factory.CreateClient();

    public HttpClient NewClientWithoutRedirects() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void Dispose()
    {
        Factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_db);
    }
}

internal static class GraphQLClient
{
    public static async Task<JsonElement> Gql(this HttpClient client, string query, object? variables = null)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static string? ErrorCode(this JsonElement body) =>
        body.TryGetProperty("errors", out var errors)
            ? errors[0].GetProperty("extensions").GetProperty("code").GetString()
            : null;

    public static JsonElement Data(this JsonElement body)
    {
        Assert.False(body.TryGetProperty("errors", out var errors), errors.ToString());
        return body.GetProperty("data");
    }

    public static async Task<JsonElement> LoginAs(this HttpClient client, string email, string password)
    {
        var body = await client.Gql(
            "mutation($i: LoginInput!) { login(input: $i) { id displayName email isAdmin } }",
            new { i = new { email, password } });
        return body.Data().GetProperty("login");
    }
}
