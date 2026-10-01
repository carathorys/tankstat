using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.ApiTests;

/// <summary>
/// Black-box contract tests against a running server (TANKSTAT_API_URL, default http://localhost:5080).
/// They pin the GraphQL schema shape the frontend depends on. Run via `mise run test:api`.
/// </summary>
public class HealthContractTests
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("TANKSTAT_API_URL") ?? "http://localhost:5080"),
    };

    private static async Task<JsonElement> Query(string query)
    {
        var response = await Http.PostAsJsonAsync("/graphql", new { query });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Health_ExposesContractFields()
    {
        var body = await Query("{ health { status version uptime databaseReachable } }");

        Assert.False(body.TryGetProperty("errors", out _), body.ToString());
        var health = body.GetProperty("data").GetProperty("health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, health.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.String, health.GetProperty("uptime").ValueKind);
        Assert.Equal(JsonValueKind.True, health.GetProperty("databaseReachable").ValueKind);
    }

    [Fact]
    public async Task Schema_QueryTypeHasHealthField()
    {
        var body = await Query("{ __type(name: \"Query\") { fields { name } } }");

        var names = body.GetProperty("data").GetProperty("__type").GetProperty("fields")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString());
        Assert.Contains("health", names);
    }
}
