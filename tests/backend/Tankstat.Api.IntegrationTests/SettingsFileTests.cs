using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Api.Settings;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The app's own settings file as a configuration source: below the environment, above the shipped appsettings, and never able to stop the
/// start (a file the app could not start with is left out, with an Error line and a public notice). The hosts of this collection run with
/// <c>Auth__Mode=None</c> in the process environment (<see cref="ApiFixture"/>), so a file faulting the authentication cannot fault here: that
/// case is in the smoke tests, where the real process starts with the file alone.
/// </summary>
[Collection(ApiCollection.Name)]
public class SettingsFileTests
{
    private const string Reporter = "Tankstat.Api.Settings.SettingsFileReporter";

    /// <summary>A host started with <paramref name="json"/> as its settings file.</summary>
    private static (TestApp App, string Path) HostWith(string json, Dictionary<string, string?>? settings = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-settings.json");
        File.WriteAllText(path, json);
        var all = settings ?? new Dictionary<string, string?> { ["Auth:Mode"] = "None" };
        all["Settings:Path"] = path;
        return (new TestApp(all), path);
    }

    private static async Task<string?> CurrencyOf(TestApp app) =>
        (await app.NewClient().Gql("{ vehicleDefaults { currency } }")).Data().GetProperty("vehicleDefaults").GetProperty("currency").GetString();

    private static async Task<List<string?>> NoticesOf(TestApp app) =>
        (await app.NewClient().Gql("{ notices { code message } }")).Data().GetProperty("notices").EnumerateArray().Select(n => n.GetProperty("code").GetString()).ToList();

    [Fact]
    public async Task TheFile_IsRead_AndTheEnvironmentStillWins()
    {
        var (app, path) = HostWith("""{ "Defaults": { "Currency": "HUF" } }""");
        try
        {
            using (app)
            {
                Assert.Equal("HUF", await CurrencyOf(app)); // nothing else sets it: the file is the source
                Assert.Empty((await NoticesOf(app)).Where(c => c != "AUTH_DISABLED"));
                var noted = Assert.Single(app.Log.From(Reporter));
                Assert.Equal((LogLevel.Information, path), (noted.Level, noted.Values["Path"]));
            }

            Environment.SetEnvironmentVariable("Defaults__Currency", "USD");
            try
            {
                using var withVariable = new TestApp(new() { ["Auth:Mode"] = "None", ["Settings:Path"] = path });
                Assert.Equal("USD", await CurrencyOf(withVariable)); // a variable beats the file, as it beats appsettings.json
            }
            finally
            {
                Environment.SetEnvironmentVariable("Defaults__Currency", null);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EveryHost_HasAFileOfItsOwn_ThatDoesNotExist()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        Assert.Equal("EUR", await CurrencyOf(app));
        Assert.False(File.Exists(app.SettingsPath));
        Assert.StartsWith(Path.GetTempPath(), app.Factory.Services.GetRequiredService<Tankstat.Application.SystemSettings.SettingsFileStatus>().Path);
        Assert.Equal(LogLevel.Debug, Assert.Single(app.Log.From(Reporter)).Level);
    }

    [Theory]
    [InlineData("""{ "Smtp": { "Port": "zzzz-not-a-port" }, "Defaults": { "Currency": "HUF" } }""", "Smtp:Port", "zzzz-not-a-port")]
    [InlineData("""{ "Logging": { "LogLevel": { "Default": "Verbosezz" } } }""", "Logging:LogLevel:Default", "Verbosezz")]
    [InlineData("""{ "Notifications": { "MaxPerHour": "0" } }""", "Notifications:MaxPerHour", null)]
    [InlineData("""{ "Sync": { "RetentionDays": "9999999999" } }""", "Sync:RetentionDays", "9999999999")] // a whole number the binder cannot fit into an int
    [InlineData("""{ "Defaults": { "RecurringWarnDays": "9999" } }""", "Defaults:RecurringWarnDays", null)] // the shape is right, the range is not
    public async Task AFileTheAppCouldNotStartWith_IsLeftOut_NamedInTheLog_AndAPublicNoticeNamesNothing(string json, string setting, string? value)
    {
        var (app, path) = HostWith(json, TestApp.StandaloneSettings());
        try
        {
            using (app)
            {
                var notices = (await app.NewClient().Gql("{ notices { code message } }")).Data().GetProperty("notices").EnumerateArray().ToList();
                var notice = Assert.Single(notices);
                Assert.Equal("SETTINGS_FILE_IGNORED", notice.GetProperty("code").GetString());
                Assert.DoesNotContain(setting.Split(':')[0], notice.GetProperty("message").GetString());

                Assert.Equal("EUR", await CurrencyOf(app)); // the whole file is left out, the good values with the bad
                var error = Assert.Single(app.Log.Entries, e => e.Level == LogLevel.Error);
                Assert.Equal(Reporter, error.Category);
                Assert.Equal(path, error.Values["Path"]);
                Assert.Contains(setting, (string)error.Values["Problems"]!);
                if (value is not null) Assert.False(app.Log.Mentions(value), value); // the setting is named, never what was written in it
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AFileThatIsNotJson_IsLeftOut_TheSameWay()
    {
        var (app, path) = HostWith("{ \"Auth\": { \"Mode\": \"Standalone\" ", TestApp.StandaloneSettings());
        try
        {
            using (app)
            {
                Assert.Equal(["SETTINGS_FILE_IGNORED"], await NoticesOf(app));
                var error = Assert.Single(app.Log.Entries, e => e.Level == LogLevel.Error);
                Assert.Contains("JSON", (string)error.Values["Problems"]!);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheCheck_FindsWhatTheHostWouldRefuse_AndNamesSettingsOnly()
    {
        var ok = SettingsCheck.Check(Configuration(new() { ["Database:Provider"] = "Sqlite", ["Auth:Mode"] = "None" }));
        Assert.Empty(ok);

        var problems = SettingsCheck.Check(Configuration(new()
        {
            ["Database:Provider"] = "PostgreSql", // no connection string: the Infrastructure validator
            ["Auth:Mode"] = "Oidc", // no authority: the Auth validator
            ["Storage:Path"] = "uploads",
            ["Storage:PhotosPath"] = "uploads/photos", // nested: the Storage validator
            ["DataProtection:KeysPath"] = " ", // the Api's key ring validator
            ["Smtp:Host"] = "mail.example.com",
            ["Smtp:From"] = "tankstat@example.com",
        }));
        Assert.Contains(problems, p => p.Contains("Database:ConnectionString"));
        Assert.Contains(problems, p => p.Contains("Auth:Oidc:Authority"));
        Assert.Contains(problems, p => p.Contains("Storage:PhotosPath"));
        Assert.Contains(problems, p => p.Contains("DataProtection:KeysPath"));

        var unreadable = SettingsCheck.Check(Configuration(new() { ["Database:Provider"] = "Oraclezz" }));
        Assert.Equal(["Database:Provider cannot be read as a value of its type."], unreadable); // the binder's words quote the value; the path is kept
        Assert.Equal(["Smtp:Port must be a whole number."], SettingsCheck.Check(Configuration(new() { ["Smtp:Port"] = "zzzz" })));
        Assert.Equal(["Logging:Console:LogLevel:Default must be one of Trace, Debug, Information, Warning, Error, Critical, None."],
            SettingsCheck.Check(Configuration(new() { ["Logging:Console:LogLevel:Default"] = "Loud" })));
        Assert.Empty(SettingsCheck.Check(Configuration(new() { ["Recognition:MaxConcurrent"] = "99" }))); // photo reading never stops the app
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
