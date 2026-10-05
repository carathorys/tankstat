using System.Net;

namespace Tankstat.Api.SmokeTests;

/// <summary>
/// Starts the real app in every authentication mode, the way a self-hoster would (environment variables only),
/// and checks it comes up and behaves like that mode. Misconfigurations must stop the app with a clear message.
/// </summary>
public class StartupSmokeTests
{
    /// <summary>Later entries override earlier ones.</summary>
    private static Dictionary<string, string?> Env(params (string Key, string? Value)[] pairs)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var (key, value) in pairs) settings[key] = value;
        return settings;
    }

    private const string Session = "{ session { mode user { email isAdmin } } notices { code } health { status databaseReachable } }";

    // ---- None ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheShippedDefaults_Start_WithTheSqliteFallbackInTheWorkingDirectory()
    {
        // No environment settings at all: only the appsettings.json that ships with the app, whatever mode it selects.
        // If the default mode needs settings that are not there (e.g. Standalone without an administrator), this fails.
        var work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"tankstat-smoke-{Guid.NewGuid():N}")).FullName;
        await using var app = await AppProcess.StartAsync(Env(), workDirectory: work, useTempDatabase: false);

        var data = (await app.Gql(Session)).Data();

        Assert.Contains(data.GetProperty("session").GetProperty("mode").GetString(), new[] { "NONE", "STANDALONE", "OIDC", "PROXY_HEADER" });
        Assert.Equal("ok", data.GetProperty("health").GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(work, "tankstat.db")), "the default SQLite database should have been created");
        // What an operator reads after a first start: the app's own line, through the shipped logging settings (Production, no overrides).
        Assert.True(await app.LogsAsync("database migrations"), app.Log);
    }

    [Fact]
    public async Task NoAuthMode_ShowsTheUnsafeNotice()
    {
        await using var app = await AppProcess.StartAsync(Env(("Auth__Mode", "None")));

        var data = (await app.Gql(Session)).Data();

        Assert.Equal("NONE", data.GetProperty("session").GetProperty("mode").GetString());
        Assert.Equal("AUTH_DISABLED", data.GetProperty("notices")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task NoneMode_ServesVehicles_EndToEnd()
    {
        await using var app = await AppProcess.StartAsync(Env(("Auth__Mode", "None")));

        var added = (await app.Gql("mutation { addVehicle(input: { name: \"Smoke car\", fuelType: PETROL }) { id } }")).Data();
        var list = (await app.Gql("{ myVehicles { name } }")).Data();

        Assert.NotNull(added.GetProperty("addVehicle").GetProperty("id").GetString());
        Assert.Equal("Smoke car", list.GetProperty("myVehicles")[0].GetProperty("name").GetString());
        Assert.Equal(1, list.GetProperty("myVehicles").GetArrayLength());
    }

    [Fact]
    public async Task TheSpaFallback_DoesNotCrashWhenTheFrontendIsNotBuilt()
    {
        await using var app = await AppProcess.StartAsync(Env(("Auth__Mode", "None")));

        var response = await app.GetAsync("/vehicles");

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(app.HasExited, app.Log);
    }

    // ---- Standalone ---------------------------------------------------------------------------------------

    private static Dictionary<string, string?> Standalone(params (string Key, string? Value)[] extra) =>
        Env([("Auth__Mode", "Standalone"), ("Auth__Standalone__AdminEmail", "root@example.com"), ("Auth__Standalone__AdminPassword", "initial-password-1"), .. extra]);

    [Fact]
    public async Task Standalone_WithAnAdministratorConfigured_Starts_AndTheAdministratorCanSignIn()
    {
        await using var app = await AppProcess.StartAsync(Standalone());

        var anonymous = (await app.Gql(Session)).Data();
        Assert.Equal("STANDALONE", anonymous.GetProperty("session").GetProperty("mode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, anonymous.GetProperty("session").GetProperty("user").ValueKind);
        Assert.Equal("UNAUTHENTICATED", (await app.Gql("{ myVehicles { id } }")).ErrorCode());

        await app.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "root@example.com", password = "initial-password-1" } });
        var signedIn = (await app.Gql(Session)).Data().GetProperty("session").GetProperty("user");
        Assert.Equal("root@example.com", signedIn.GetProperty("email").GetString());
        Assert.True(signedIn.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task WithScopesOn_TheConsoleNamesTheSignedInUser_OnTheLinesOfHisRequests()
    {
        // The unit and integration tests read scopes as data; only the process shows how the console prints one (a scope that is a
        // dictionary is printed as its type name). The formatter has to be named for its options to apply.
        await using var app = await AppProcess.StartAsync(Standalone(
            ("Logging__LogLevel__Tankstat", "Debug"), ("Logging__Console__FormatterName", "simple"), ("Logging__Console__FormatterOptions__IncludeScopes", "true")));
        var login = (await app.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "root@example.com", password = "initial-password-1" } })).Data().GetProperty("login");
        var id = login.GetProperty("id").GetString()!;

        await app.Gql("{ myVehicles { id } }"); // a request of the signed-in user: the line that times it is written inside the scope

        Assert.True(await app.LogsAsync($"UserId: {id}"), app.Log);
        Assert.DoesNotContain("Dictionary", app.Log);
    }

    [Fact]
    public async Task Standalone_StartsAgainOnAnExistingDatabase_WithoutTheBootstrapSettings()
    {
        var first = await AppProcess.StartAsync(Standalone());
        var work = first.WorkDirectory;
        await first.StopAsync();

        // Second start: the administrator already exists in the database, so no bootstrap settings are needed.
        var settings = Env(("Auth__Mode", "Standalone"), ("Database__ConnectionString", $"Data Source={first.DatabasePath}"));
        var second = await AppProcess.StartAsync(settings, workDirectory: work, useTempDatabase: false);
        try
        {
            await second.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "root@example.com", password = "initial-password-1" } });
            Assert.Equal("root@example.com", (await second.Gql(Session)).Data().GetProperty("session").GetProperty("user").GetProperty("email").GetString());
            Assert.True(await second.LogsAsync("up to date"), second.Log); // nothing to migrate this time, and the log says so
            Assert.DoesNotContain("Applying", second.Log);
        }
        finally
        {
            await second.DisposeAsync();
            await first.DisposeAsync();
        }
    }

    [Fact]
    public async Task Standalone_WithoutAnAdministrator_StopsAtStartupWithAnActionableMessage()
    {
        // The behaviour reported as "crash on startup": nothing configured and no administrator in the database.
        // Today this is a deliberate fail-fast; this test pins down exactly what happens.
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Env(("Auth__Mode", "Standalone"), ("Auth__Standalone__AdminEmail", ""), ("Auth__Standalone__AdminPassword", "")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("no administrator exists", log);
        Assert.Contains("Auth:Standalone:AdminEmail", log);
        Assert.Contains("Auth__Standalone__AdminEmail", log);
    }

    [Fact]
    public async Task Standalone_WithAWeakAdministratorPassword_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Standalone(("Auth__Standalone__AdminPassword", "weak")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("at least", log);
    }

    [Fact]
    public async Task Standalone_WithSmtpButNoPublicUrl_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Standalone(("Smtp__Host", "mail.example.com"), ("Smtp__From", "tank@example.com")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Auth:PublicUrl", log);
    }

    [Fact]
    public async Task Standalone_WithSmtpAndPublicUrl_Starts()
    {
        await using var app = await AppProcess.StartAsync(Standalone(("Smtp__Host", "mail.example.com"), ("Smtp__From", "tank@example.com"), ("Auth__PublicUrl", "https://tank.example.com")));

        Assert.Equal("STANDALONE", (await app.Gql(Session)).Data().GetProperty("session").GetProperty("mode").GetString());
    }

    // ---- OIDC ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Oidc_Starts_WithoutContactingTheProvider()
    {
        // The authority does not exist: startup must not depend on the identity provider being reachable.
        await using var app = await AppProcess.StartAsync(Env(
            ("Auth__Mode", "Oidc"), ("Auth__Oidc__Authority", "https://id.invalid"), ("Auth__Oidc__ClientId", "tankstat"), ("Auth__Oidc__ClientSecret", "secret")));

        var data = (await app.Gql(Session)).Data();
        Assert.Equal("OIDC", data.GetProperty("session").GetProperty("mode").GetString());
        Assert.Empty(data.GetProperty("notices").EnumerateArray());
        Assert.Equal("UNAUTHENTICATED", (await app.Gql("{ myVehicles { id } }")).ErrorCode());
        Assert.NotEqual(HttpStatusCode.NotFound, (await app.GetAsync("/auth/oidc/login")).StatusCode); // the login endpoint exists in this mode
    }

    [Fact]
    public async Task Oidc_WithoutProviderSettings_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Env(("Auth__Mode", "Oidc")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Auth:Oidc:Authority", log);
    }

    // ---- Proxy header -------------------------------------------------------------------------------------

    private static Dictionary<string, string?> Proxy(string trusted) =>
        Env(("Auth__Mode", "ProxyHeader"), ("Auth__ProxyHeader__TrustedProxies__0", trusted), ("Auth__AdminEmails__0", "boss@example.com"));

    private static Dictionary<string, string> User(string name, string email) =>
        new() { ["X-Forwarded-User"] = name, ["X-Forwarded-Email"] = email };

    [Fact]
    public async Task ProxyHeader_TrustsTheHeaderFromTheConfiguredProxy()
    {
        await using var app = await AppProcess.StartAsync(Proxy("127.0.0.0/8")); // the test connects from loopback

        var anonymous = (await app.Gql(Session)).Data();
        Assert.Equal("PROXY_HEADER", anonymous.GetProperty("session").GetProperty("mode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, anonymous.GetProperty("session").GetProperty("user").ValueKind);

        var boss = (await app.Gql(Session, headers: User("boss", "boss@example.com"))).Data().GetProperty("session").GetProperty("user");
        Assert.Equal("boss@example.com", boss.GetProperty("email").GetString());
        Assert.True(boss.GetProperty("isAdmin").GetBoolean());
        Assert.Null((await app.Gql("{ myVehicles { id } }", headers: User("boss", "boss@example.com"))).ErrorCode());
    }

    [Fact]
    public async Task ProxyHeader_IgnoresTheHeaderFromAnyoneElse()
    {
        await using var app = await AppProcess.StartAsync(Proxy("10.0.0.0/8")); // the test connects from 127.0.0.1: not trusted

        var forged = (await app.Gql(Session, headers: User("boss", "boss@example.com"))).Data().GetProperty("session").GetProperty("user");

        Assert.Equal(System.Text.Json.JsonValueKind.Null, forged.ValueKind);
        Assert.Equal("UNAUTHENTICATED", (await app.Gql("{ myVehicles { id } }", headers: User("boss", "boss@example.com"))).ErrorCode());
    }

    [Fact]
    public async Task ProxyHeader_WithoutTrustedProxies_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Env(("Auth__Mode", "ProxyHeader")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("TrustedProxies", log);
    }

    // ---- Photo reading (optional: it never stops the app) ---------------------------------------------------

    private const string Recognition = "{ recognitionStatus { available } }";

    private static bool Available(System.Text.Json.JsonElement body) => body.Data().GetProperty("recognitionStatus").GetProperty("available").GetBoolean();

    [Fact]
    public async Task PhotoReading_IsOffByDefault_WithoutAWord()
    {
        await using var app = await AppProcess.StartAsync(Env(("Auth__Mode", "None")));

        Assert.False(Available(await app.Gql(Recognition)));
        Assert.DoesNotContain("Photo reading", app.Log);
    }

    [Fact]
    public async Task PhotoReading_WithAModelServerThatCannotBeReached_StartsAndSaysItIsUnavailable()
    {
        await using var app = await AppProcess.StartAsync(Env(
            ("Auth__Mode", "None"),
            ("Recognition__Provider", "OpenAiCompatible"),
            ("Recognition__OpenAiCompatible__BaseUrl", "http://127.0.0.1:9/v1"), // nothing listens there
            ("Recognition__OpenAiCompatible__Model", "smoke")));

        Assert.False(Available(await app.Gql(Recognition)));
        Assert.True(await app.LogsAsync("Photo reading uses the OpenAiCompatible provider"), app.Log);
        Assert.True(await app.LogsAsync("model smoke at http://127.0.0.1:9/v1"), app.Log); // which model, where: what an operator looks for first
        Assert.True(await app.LogsAsync("System prompt: built-in ("), app.Log);
        Assert.False(app.HasExited);
    }

    [Fact]
    public async Task PhotoReading_WithIncompleteSettings_IsTurnedOffWithAWarning_AndTheAppStarts()
    {
        await using var app = await AppProcess.StartAsync(Env(("Auth__Mode", "None"), ("Recognition__Provider", "OpenAiCompatible"), ("Recognition__MaxConcurrent", "many")));

        Assert.False(Available(await app.Gql(Recognition)));
        Assert.True(await app.LogsAsync("Photo reading is turned off"), app.Log);
        Assert.False(app.HasExited);
    }

    // ---- Misconfiguration ---------------------------------------------------------------------------------

    [Fact]
    public async Task AnUnknownAuthMode_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Env(("Auth__Mode", "Kerberos")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Auth:Mode", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownDatabaseProvider_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Env(("Database__Provider", "Oracle")));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Oracle", log);
    }

    [Fact]
    public async Task ANetworkDatabaseWithoutAConnectionString_StopsAtStartup()
    {
        var (exitCode, log) = await AppProcess.RunUntilExitAsync(Env(("Database__Provider", "PostgreSql")), useTempDatabase: false);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Database:ConnectionString", log);
    }
}
