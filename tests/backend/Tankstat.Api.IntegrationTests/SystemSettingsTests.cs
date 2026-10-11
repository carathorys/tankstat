using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The system settings an administrator manages through GraphQL: every setting with its source, changes written into the host's own
/// settings file (waiting for a restart), refusals that write nothing, secrets never shown or logged, and the restart that reaches the host's
/// restart service (a fake here: the real one would stop the test host).
/// </summary>
[Collection(ApiCollection.Name)]
public class SystemSettingsTests
{
    private const string Service = "Tankstat.Application.SystemSettings.SystemSettingsService";

    private const string Query = "{ systemSettings { file { path exists ignored problems } restartPending groups { name settings { key kind choices value isSet isSecret source editable restartPending } } } }";

    private const string Update = "mutation($c: [SystemSettingChangeInput!]!) { updateSystemSettings(changes: $c) { restartPending file { exists } groups { name settings { key value isSet isSecret source editable restartPending } } } }";

    private static async Task<HttpClient> Admin(TestApp app)
    {
        var c = app.NewClient();
        await c.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);
        return c;
    }

    private static JsonElement Setting(JsonElement settings, string key) =>
        settings.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("settings").EnumerateArray()).Single(s => s.GetProperty("key").GetString() == key);

    private static string? ErrorKey(JsonElement body) => body.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString();

    [Fact]
    public async Task Administrators_SeeEverySetting_WithItsSource_OthersNothing()
    {
        using var app = TestApp.Standalone();
        var settings = (await (await Admin(app)).Gql(Query)).Data().GetProperty("systemSettings");

        Assert.Equal(["AUTH", "SMTP", "DEFAULTS", "NOTIFICATIONS", "SYNC", "PHOTO_READING", "LOGGING"], settings.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
        var mode = Setting(settings, "Auth:Mode");
        Assert.Equal(("ENVIRONMENT", false, "Standalone", "CHOICE"), (mode.GetProperty("source").GetString(), mode.GetProperty("editable").GetBoolean(), mode.GetProperty("value").GetString(), mode.GetProperty("kind").GetString())); // the host's own settings sit above the file, as the environment does
        Assert.Equal(["None", "Standalone", "Oidc", "ProxyHeader"], mode.GetProperty("choices").EnumerateArray().Select(c => c.GetString()));
        var currency = Setting(settings, "Defaults:Currency");
        Assert.Equal(("DEFAULT", true, false, JsonValueKind.Null), (currency.GetProperty("source").GetString(), currency.GetProperty("editable").GetBoolean(), currency.GetProperty("isSet").GetBoolean(), currency.GetProperty("value").ValueKind));
        var level = Setting(settings, "Logging:LogLevel:Default");
        Assert.Equal(("SHIPPED", true, "Information"), (level.GetProperty("source").GetString(), level.GetProperty("editable").GetBoolean(), level.GetProperty("value").GetString())); // appsettings.json: a default the file may override
        var file = settings.GetProperty("file");
        Assert.Equal((app.SettingsPath, false, false), (file.GetProperty("path").GetString(), file.GetProperty("exists").GetBoolean(), file.GetProperty("ignored").GetBoolean()));
        Assert.False(settings.GetProperty("restartPending").GetBoolean());

        var users = await app.Users();
        Assert.Equal("FORBIDDEN", (await users.Alice.Gql(Query)).ErrorCode());
        Assert.Equal("FORBIDDEN", (await users.Alice.Gql("mutation { restartServer }")).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await app.NewClient().Gql(Query)).ErrorCode());
        Assert.Equal(0, app.Restarts.Count);

        using var none = new TestApp(new() { ["Auth:Mode"] = "None" });
        Assert.Equal("FORBIDDEN", (await none.NewClient().Gql(Query)).ErrorCode()); // mode None has no administrator: the environment changes its mode
    }

    [Fact]
    public async Task Changes_AreWrittenIntoTheFile_AndWaitForARestart()
    {
        using var app = TestApp.Standalone();
        var admin = await Admin(app);
        const string secret = "hunter2-zzzz-secret";

        var answer = (await admin.Gql(Update, new { c = new object[] { new { key = "Defaults:Currency", value = "HUF" }, new { key = "Smtp:Password", value = secret } } })).Data().GetProperty("updateSystemSettings");

        var currency = Setting(answer, "Defaults:Currency");
        Assert.Equal(("HUF", "DEFAULT", true), (currency.GetProperty("value").GetString(), currency.GetProperty("source").GetString(), currency.GetProperty("restartPending").GetBoolean())); // the running value is still the default
        var password = Setting(answer, "Smtp:Password");
        Assert.Equal((JsonValueKind.Null, true, true), (password.GetProperty("value").ValueKind, password.GetProperty("isSet").GetBoolean(), password.GetProperty("isSecret").GetBoolean()));
        Assert.True(answer.GetProperty("restartPending").GetBoolean());
        Assert.True(answer.GetProperty("file").GetProperty("exists").GetBoolean());
        Assert.Equal("EUR", (await admin.Gql("{ vehicleDefaults { currency } }")).Data().GetProperty("vehicleDefaults").GetProperty("currency").GetString()); // nothing reloads

        var written = SettingsFileStore.Read(app.SettingsPath);
        Assert.Equal(("HUF", secret), (written["Defaults:Currency"], written["Smtp:Password"]));
        var line = Assert.Single(app.Log.From(Service), e => e.Level == LogLevel.Information);
        Assert.Contains("Smtp:Password", (string)line.Values["Keys"]!);
        Assert.False(app.Log.Mentions(secret), secret);

        await admin.Gql(Update, new { c = new object[] { new { key = "Defaults:Currency", value = "USD" } } });
        Assert.Equal("HUF", SettingsFileStore.Read(app.SettingsPath + ".bak")["Defaults:Currency"]); // the previous file, kept beside it
        Assert.Equal(secret, SettingsFileStore.Read(app.SettingsPath)["Smtp:Password"]); // the secret stayed: the second change did not mention it

        using var restarted = new TestApp(TestApp.StandaloneSettings(new() { ["Settings:Path"] = app.SettingsPath })); // the next start reads the file
        Assert.Equal("USD", (await restarted.NewClient().Gql("{ vehicleDefaults { currency } }")).Data().GetProperty("vehicleDefaults").GetProperty("currency").GetString());
        var now = Setting((await (await Admin(restarted)).Gql(Query)).Data().GetProperty("systemSettings"), "Defaults:Currency");
        Assert.Equal(("FILE", "USD", false), (now.GetProperty("source").GetString(), now.GetProperty("value").GetString(), now.GetProperty("restartPending").GetBoolean()));
    }

    [Fact]
    public async Task WhatCannotBeTaken_IsRefusedByKey_AndWritesNothing()
    {
        using var app = TestApp.Standalone();
        var admin = await Admin(app);

        async Task<string?> Refusal(string key, string value)
        {
            var body = await admin.Gql(Update, new { c = new object[] { new { key = "Defaults:Currency", value = "HUF" }, new { key, value } } });
            Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
            return ErrorKey(body);
        }

        Assert.Equal("settings.unknownKey", await Refusal("Database:Provider", "PostgreSql"));
        Assert.Equal("settings.readOnly", await Refusal("Auth:Mode", "None"));
        Assert.Equal("settings.invalid", await Refusal("Smtp:Port", "zzzz-not-a-port"));
        Assert.Equal("settings.invalid", await Refusal("Notifications:MaxPerHour", "0"));
        Assert.Equal("settings.invalid", await Refusal("Logging:LogLevel:Microsoft.AspNetCore", "Debug"));

        Assert.False(File.Exists(app.SettingsPath)); // all or nothing: the currency did not land either
        Assert.False(app.Log.Mentions("zzzz-not-a-port"));
        Assert.Empty(app.Log.From(Service).Where(e => e.Level >= LogLevel.Information));
    }

    [Fact]
    public async Task RestartServer_ReachesTheHostsRestart_AndIsLogged()
    {
        using var app = TestApp.Standalone();
        var admin = await Admin(app);

        Assert.True((await admin.Gql("mutation { restartServer }")).Data().GetProperty("restartServer").GetBoolean());

        Assert.Equal(1, app.Restarts.Count);
        var line = Assert.Single(app.Log.From(Service), e => e.Level == LogLevel.Information);
        Assert.True(line.Values.ContainsKey("AdminId"));
    }
}

/// <summary>A start in another authentication mode signs every device out: a session issued in one mode never works in the next, nor after a switch back.</summary>
[Collection(ApiCollection.Name)]
public class ModeChangeSignOutTests
{
    private const string Service = "Tankstat.Application.SystemSettings.InstanceStartService";

    private static readonly Dictionary<string, string?> OidcSettings = new()
    {
        ["Auth:Mode"] = "Oidc",
        ["Auth:Oidc:Authority"] = "https://id.example.com",
        ["Auth:Oidc:ClientId"] = "tankstat",
        ["Auth:Oidc:ClientSecret"] = "secret",
    };

    private static void StubDiscovery(IServiceCollection s) => s.PostConfigure<OpenIdConnectOptions>("oidc", o =>
        o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
        {
            Issuer = "https://id.example.com",
            AuthorizationEndpoint = "https://id.example.com/authorize",
            TokenEndpoint = "https://id.example.com/token",
        }));

    private static async Task<string?> UserEmail(HttpClient c)
    {
        var user = (await c.Gql("{ session { user { email } } }")).Data().GetProperty("session").GetProperty("user");
        return user.ValueKind == JsonValueKind.Null ? null : user.GetProperty("email").GetString();
    }

    [Fact]
    public async Task AStartInAnotherMode_SignsEveryDeviceOut_ForGood()
    {
        using var data = new SharedData();
        var cookies = await data.StandaloneAdminCookiesAsync();

        using (var oidc = data.Host(OidcSettings, StubDiscovery))
        {
            Assert.Null(await UserEmail(oidc.NewClientHolding(cookies))); // the Standalone sign-in does not carry over
            var line = Assert.Single(oidc.Log.From(Service), e => e.Level == LogLevel.Information);
            Assert.Equal(("Standalone", "Oidc", 1), (line.Values["Before"], line.Values["After"], line.Values["Count"]));
        }

        using var standalone = data.Host(TestApp.StandaloneSettings());
        Assert.Null(await UserEmail(standalone.NewClientHolding(cookies))); // nor does it come back with the mode
        Assert.Equal(TestApp.AdminEmail, (await standalone.NewClient().LoginAs(TestApp.AdminEmail, TestApp.AdminPassword)).GetProperty("email").GetString()); // a new sign-in works
    }

    [Fact]
    public async Task AStartInTheSameMode_SaysNothing_AndKeepsEveryoneSignedIn()
    {
        using var data = new SharedData();
        var cookies = await data.StandaloneAdminCookiesAsync();

        using var again = data.Host(TestApp.StandaloneSettings());

        Assert.Equal(TestApp.AdminEmail, await UserEmail(again.NewClientHolding(cookies)));
        Assert.Empty(again.Log.From(Service).Where(e => e.Level >= LogLevel.Information));
    }
}
