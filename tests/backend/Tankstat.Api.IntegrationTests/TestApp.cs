using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.TestSupport;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// A host with its own settings and throwaway SQLite database. Settings are passed in memory (they outrank
/// appsettings.json and environment variables), so several hosts with different auth modes can be tested.
/// </summary>
internal sealed class TestApp : IDisposable
{
    /// <summary>
    /// A database with every migration applied, made once per test run (by a host of its own) and copied for each host: a host would
    /// otherwise apply all of them to its new file, which was most of the time a test took. The hosts find it up to date and start at once.
    /// </summary>
    private static readonly Lazy<string> Template = new(CreateTemplate);

    private readonly string _db = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}.db");

    /// <summary>Where uploaded pictures of this host go.</summary>
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-uploads");

    /// <summary>Where the photos of logs (and their drafts) of this host go.</summary>
    public string PhotosPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-photos");

    /// <summary>Where this host keeps the keys that protect its cookies.</summary>
    public string KeysPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-keys");

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>Everything this host logged, from its first line on.</summary>
    public CapturedLog Log { get; } = new();

    /// <param name="host">Last word on the web host, e.g. a web root with static files to serve.</param>
    /// <param name="migrated">False: a new, empty database, so the host applies every migration itself (the template is made that way).</param>
    public TestApp(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null, Action<IWebHostBuilder>? host = null, bool migrated = true)
    {
        if (migrated) File.Copy(Template.Value, _db);
        var all = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={_db}",
            ["Storage:Path"] = UploadsPath,
            ["Storage:PhotosPath"] = PhotosPath,
            ["DataProtection:KeysPath"] = KeysPath, // the cookies' keys of this host only, never in the build output
            ["Logging:LogLevel:Tankstat"] = "Debug", // the Debug lines are part of what is tested
        };
        foreach (var (key, value) in settings) all[key] = value;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development"); // introspection etc.; never uses the dev appsettings of a real database
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(all));
            b.ConfigureLogging(l => l.AddProvider(Log));
            b.ConfigureServices(s => services?.Invoke(s));
            host?.Invoke(b);
        });
    }

    public static TestApp Standalone(Dictionary<string, string?>? extra = null, Action<IServiceCollection>? services = null) => new(new Dictionary<string, string?>
    {
        ["Auth:Mode"] = "Standalone",
        ["Auth:Standalone:AdminEmail"] = "root@example.com",
        ["Auth:Standalone:AdminPassword"] = "initial-password-1",
    }.Concat(extra ?? []).ToDictionary(kv => kv.Key, kv => kv.Value), services);

    /// <summary>A client with its own cookie jar, i.e. its own browser session.</summary>
    public HttpClient NewClient() => Factory.CreateClient();

    public HttpClient NewClientWithoutRedirects() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Like <see cref="NewClientWithoutRedirects"/>, over https: the OIDC correlation cookie is Secure and a browser sends it only then.</summary>
    public HttpClient NewHttpsClientWithoutRedirects() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    public void Dispose()
    {
        Factory.Dispose();
        // Only this database's pooled connections: ClearAllPools would also close the ones of tests running in parallel.
        using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
        File.Delete(_db);
        if (Directory.Exists(UploadsPath)) Directory.Delete(UploadsPath, recursive: true);
        if (Directory.Exists(PhotosPath)) Directory.Delete(PhotosPath, recursive: true);
        if (Directory.Exists(KeysPath)) Directory.Delete(KeysPath, recursive: true);
    }

    /// <summary>Starts a host on a new database (no auth, nothing else to set up), lets it migrate, and keeps the file until the run ends.</summary>
    private static string CreateTemplate()
    {
        string path;
        using (var app = new TestApp(new Dictionary<string, string?> { ["Auth:Mode"] = "None" }, migrated: false))
        {
            _ = app.Factory.Services; // starts the host: the migrations run as it starts
            path = Path.Combine(Path.GetTempPath(), $"tankstat-it-template-{Guid.NewGuid():N}.db");
            app.Factory.Dispose();
            using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={app._db}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
            File.Copy(app._db, path);
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(path);
        return path;
    }
}

/// <summary>Two signed-in users of a Standalone host, created by its administrator, who stays signed in too.</summary>
internal sealed record TwoUsers(HttpClient Admin, HttpClient Alice, string AliceId, HttpClient Bob, string BobId);

internal static class TestAppUsers
{
    /// <summary>Alice and Bob, each in their own browser session (passwords <c>alice-password-1</c> / <c>bob-password-1</c>).</summary>
    public static async Task<TwoUsers> Users(this TestApp app)
    {
        var admin = app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        async Task<(HttpClient Client, string Id)> Create(string name)
        {
            var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }",
                new { i = new { email = $"{name}@example.com", displayName = name, isAdmin = false } })).Data().GetProperty("createUser");
            await app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }",
                new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = name + "-password-1" } });
            var client = app.NewClient();
            await client.LoginAs($"{name}@example.com", name + "-password-1");
            return (client, created.GetProperty("user").GetProperty("id").GetString()!);
        }
        var (alice, aliceId) = await Create("alice");
        var (bob, bobId) = await Create("bob");
        return new TwoUsers(admin, alice, aliceId, bob, bobId);
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
