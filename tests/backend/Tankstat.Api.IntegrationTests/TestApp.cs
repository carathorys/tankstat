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

    /// <summary>A migrated database at <paramref name="path"/>, from the template every host starts from.</summary>
    public static void CopyMigratedDatabase(string path) => File.Copy(Template.Value, path);

    private readonly string _db = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}.db");

    /// <summary>Where uploaded pictures of this host go.</summary>
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-uploads");

    /// <summary>Where the photos of logs (and their drafts) of this host go.</summary>
    public string PhotosPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-photos");

    /// <summary>Where this host keeps the keys that protect its cookies, unless its settings name another folder (<see cref="SharedData"/>).</summary>
    public string KeysPath { get; } = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-keys");

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>Everything this host logged, from its first line on.</summary>
    public CapturedLog Log { get; } = new();

    /// <param name="settings">Over the host's own; a <c>Database:ConnectionString</c> among them is the database to use, nothing is copied then.</param>
    /// <param name="host">Last word on the web host, e.g. a web root with static files to serve.</param>
    /// <param name="migrated">False: a new, empty database, so the host applies every migration itself (the template is made that way).</param>
    public TestApp(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null, Action<IWebHostBuilder>? host = null, bool migrated = true)
    {
        if (migrated && !settings.ContainsKey("Database:ConnectionString")) CopyMigratedDatabase(_db);
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

    /// <summary>The bootstrap administrator of a Standalone host (<see cref="StandaloneSettings"/>).</summary>
    public const string AdminEmail = "root@example.com";
    public const string AdminPassword = "initial-password-1";

    /// <summary>The settings of a Standalone host with the bootstrap administrator, <paramref name="extra"/> over them.</summary>
    public static Dictionary<string, string?> StandaloneSettings(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone",
            ["Auth:Standalone:AdminEmail"] = AdminEmail,
            ["Auth:Standalone:AdminPassword"] = AdminPassword,
        };
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        return settings;
    }

    public static TestApp Standalone(Dictionary<string, string?>? extra = null, Action<IServiceCollection>? services = null) => new(StandaloneSettings(extra), services);

    /// <summary>A client with its own cookie jar, i.e. its own browser session.</summary>
    public HttpClient NewClient() => Factory.CreateClient();

    public HttpClient NewClientWithoutRedirects() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Like <see cref="NewClientWithoutRedirects"/>, over https: the OIDC correlation cookie is Secure and a browser sends it only then.</summary>
    public HttpClient NewHttpsClientWithoutRedirects() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    /// <summary>A client without a jar that sends <paramref name="cookies"/> (a <c>Cookie</c> header) with every request: a browser still holding the cookies of an earlier sign-in.</summary>
    public HttpClient NewClientHolding(string cookies)
    {
        var c = Factory.CreateDefaultClient();
        c.DefaultRequestHeaders.Add("Cookie", cookies);
        return c;
    }

    /// <summary>Closes the pooled connections to the database at <paramref name="path"/>, so the file can go. Only that database's: ClearAllPools would also close the ones of tests running in parallel.</summary>
    public static void ReleaseDatabase(string path)
    {
        using var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
    }

    public void Dispose()
    {
        Factory.Dispose();
        ReleaseDatabase(_db);
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
            ReleaseDatabase(app._db);
            File.Copy(app._db, path);
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(path);
        return path;
    }
}

/// <summary>
/// One database, one key ring and one set of upload folders for several hosts in turn: the same instance restarted with other settings (another
/// auth mode, say). The database starts as a copy of the migrated template, every host gets the shared paths over its own settings, and starting
/// a host lets go of the connections the host before held, so dispose that one first.
/// </summary>
internal sealed class SharedData : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-shared");

    public string DatabasePath => _root + ".db";
    public string KeysPath => _root + "-keys";
    public string UploadsPath => _root + "-uploads";
    public string PhotosPath => _root + "-photos";

    public SharedData() => TestApp.CopyMigratedDatabase(DatabasePath);

    public TestApp Host(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null, Action<IWebHostBuilder>? host = null)
    {
        TestApp.ReleaseDatabase(DatabasePath); // the pooled connections of the host before
        return new(new Dictionary<string, string?>(settings)
        {
            ["Database:ConnectionString"] = $"Data Source={DatabasePath}",
            ["DataProtection:KeysPath"] = KeysPath,
            ["Storage:Path"] = UploadsPath,
            ["Storage:PhotosPath"] = PhotosPath,
        }, services, host);
    }

    /// <summary>
    /// Signs the bootstrap administrator in on a Standalone host over this data and gives back what the browser then holds, as a
    /// <c>Cookie</c> header with both cookies (what a browser sends to <c>/auth/token/...</c>; <c>/graphql</c> reads the access cookie only):
    /// a sign-in made before the instance was restarted in another mode.
    /// </summary>
    public async Task<string> StandaloneAdminCookiesAsync()
    {
        using var standalone = Host(TestApp.StandaloneSettings());
        var login = await standalone.Factory.CreateDefaultClient().PostAsJsonAsync("/graphql", new // the cookies are read off the response: no jar needed
        {
            query = "mutation($i: LoginInput!) { login(input: $i) { id } }",
            variables = new { i = new { email = TestApp.AdminEmail, password = TestApp.AdminPassword } },
        });
        (await login.Content.ReadFromJsonAsync<JsonElement>()).Data().GetProperty("login"); // signed in
        return string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }

    public void Dispose()
    {
        TestApp.ReleaseDatabase(DatabasePath);
        File.Delete(DatabasePath);
        foreach (var folder in new[] { KeysPath, UploadsPath, PhotosPath })
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
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
