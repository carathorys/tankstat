using Microsoft.AspNetCore.Builder;
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
    [InlineData("""{ "Logging": { "Console": { "FormatterOptions": { "UseUtcTimestamp": "zzzz-yes" } } } }""", "Logging:Console:FormatterOptions:UseUtcTimestamp", "zzzz-yes")] // the console's options are bound as the host is built
    [InlineData("""{ "Logging": { "LogLevel": { "Microsoft": { "AspNetCore": "Loudzz" } } } }""", "Logging:LogLevel:Microsoft:AspNetCore", "Loudzz")] // a nested level is read too
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
        Assert.Equal(["Database:Provider cannot be read as a value of its type."], // a value holding the binder's own words does not pass for the path
            SettingsCheck.Check(Configuration(new() { ["Database:Provider"] = "zzzz at 'yyyy' xxxx" })));
        Assert.Equal(["Smtp:Port must be a whole number."], SettingsCheck.Check(Configuration(new() { ["Smtp:Port"] = "zzzz" })));
        Assert.Equal(["Logging:Console:LogLevel:Default must be one of Trace, Debug, Information, Warning, Error, Critical, None."],
            SettingsCheck.Check(Configuration(new() { ["Logging:Console:LogLevel:Default"] = "Loud" })));
        Assert.Equal(["Logging:LogLevel:Default must be one of Trace, Debug, Information, Warning, Error, Critical, None."], // once, though two checks look at it
            SettingsCheck.Check(Configuration(new() { ["Logging:LogLevel:Default"] = "Verbosezz" })));
        Assert.Equal(["Logging:LogLevel:Microsoft:AspNetCore must be one of Trace, Debug, Information, Warning, Error, Critical, None."],
            SettingsCheck.Check(Configuration(new() { ["Logging:LogLevel:Microsoft:AspNetCore"] = "Loud" }))); // nested, as the logging filter reads it
        Assert.Equal(["Logging:Console:FormatterOptions:UseUtcTimestamp cannot be read as a value of its type."],
            SettingsCheck.Check(Configuration(new() { ["Logging:Console:FormatterName"] = "simple", ["Logging:Console:FormatterOptions:UseUtcTimestamp"] = "zzzz" })));
        Assert.Equal(["Logging:CaptureScopes cannot be read as a value of its type."], SettingsCheck.Check(Configuration(new() { ["Logging:CaptureScopes"] = "zzzz" })));
        Assert.Empty(SettingsCheck.Check(Configuration(new() { ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "" }))); // the logging filter skips an empty level

        Assert.Empty(SettingsCheck.Check(Configuration(new() { ["Recognition:MaxConcurrent"] = "99" }))); // photo reading never stops the app
        Assert.Empty(SettingsCheck.Check(Configuration(new() { ["Recognition:MinConfidence"] = "zzzz", ["Recognition:Provider"] = "" }))); // not even with a wrong shape
        Assert.Empty(SettingsCheck.Check(Configuration(new() { ["Logging:LogLevel:Microsoft.AspNetCore"] = "Information" }))); // the floor is the page's rule, not the start's
        Assert.Empty(SettingsCheck.Check(Configuration(new() { ["Smtp:Security"] = "Ssl" }))); // the old spelling: the sender runs it as Auto
    }

    [Fact]
    public void WhatTheEnvironmentGetsWrongOnItsOwn_NeitherKeepsABrokenFileIn_NorLeavesAGoodOneOut()
    {
        // The app starts with this value (the sender falls back to Auto), yet the check names it: a problem the environment has on its own.
        const string environment = "--Smtp:Security=zzzz-unknown";

        WithFile("""{ "Notifications": { "MaxPerHour": "0" } }""", path =>
        {
            var file = Prepared(path, environment).File;
            Assert.True(file.Status.Ignored); // it would stop the start: it stays out
            Assert.Equal(["Notifications:MaxPerHour must be at least 1."], file.Status.Problems); // and only what it brings is held against it
        });
        WithFile("""{ "Defaults": { "Currency": "HUF" } }""", path =>
        {
            var (file, builder) = Prepared(path, environment);
            Assert.False(file.Status.Ignored);
            Assert.Equal("HUF", builder.Configuration["Defaults:Currency"]);
        });
        WithFile("{ not json", path => Assert.Equal(["The file is not valid JSON."], Prepared(path, environment).File.Status.Problems)); // left out, not thrown at the start
    }

    [Fact]
    public void AnEnvironmentThatCannotStartOnItsOwn_KeepsAReadableFileIn_SoTheHostNamesItsMistakes()
    {
        // The OIDC mode set above the file, its settings in the file only, and the file wrong besides: the start fails either way, and with the
        // file in place the host names the file's own mistake instead of asking for an authority the file holds.
        WithFile("""{ "Auth": { "Oidc": { "Authority": "https://id.example.com", "ClientId": "tankstat", "ClientSecret": "zzzz" } }, "Notifications": { "MaxPerHour": "0" } }""",
            path => Assert.False(Prepared(path, "--Auth:Mode=Oidc").File.Status.Ignored));
        // An environment broken on its own (a log level the host refuses as it is built): a broken file goes in too, the start fails either way.
        WithFile("""{ "Notifications": { "MaxPerHour": "0" } }""", path => Assert.False(Prepared(path, "--Logging:LogLevel:Default=Verbose").File.Status.Ignored));
        WithFile("{ not json", path => Assert.True(Prepared(path, "--Auth:Mode=Oidc").File.Status.Ignored)); // one it cannot read stays out: inserting it would throw
    }

    [Fact]
    public void AFileThatCannotBeRead_IsLeftOut_AndDoesNotStopTheStart()
    {
        WithFile("""{ "Defaults": { "Currency": "HUF" } }""", path =>
        {
            if (OperatingSystem.IsWindows()) return; // Unix file modes
            File.SetUnixFileMode(path, UnixFileMode.None); // a file the app's user may not read, as a root-owned 0600 one in a volume
            try
            {
                if (Readable(path)) return; // root reads it anyway: nothing to show
                var file = Prepared(path).File;
                Assert.True(file.Status.Ignored);
                Assert.Equal(["The file cannot be read."], file.Status.Problems);
            }
            finally
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        });
    }

    [Fact]
    public void AFileWhoseNameStartsWithADot_IsRead_LikeAnyOther()
    {
        var path = Path.Combine(Path.GetTempPath(), $".tankstat-it-{Guid.NewGuid():N}-settings.json");
        File.WriteAllText(path, """{ "Defaults": { "Currency": "HUF" } }""");
        try
        {
            var (file, builder) = Prepared(path);
            Assert.False(file.Status.Ignored);
            Assert.Equal("HUF", builder.Configuration["Defaults:Currency"]); // the default file provider would hide it and read nothing
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LeavingTheFileOut_NeverTurnsAuthenticationOff_WhenTheFileAsksForASignIn()
    {
        // Without the file the mode is None and nothing above the file holds it: the file decides it, and leaving it out would let every
        // visitor in as the owner of all data. The start stops instead, naming the file's mistakes and the way out.
        WithFile("""{ "Auth": { "Mode": "Standalone" }, "Notifications": { "MaxPerHour": "0" } }""", path =>
        {
            var refused = Assert.Throws<SettingsFileRefusedException>(() => SettingsFile.Prepare(Shipped(path)));
            Assert.Equal(["Notifications:MaxPerHour must be at least 1."], refused.Problems);
            Assert.Contains(path, refused.Message);
            Assert.Contains("Auth__Mode", refused.Message);
        });
        WithFile("""{ "Auth": { "Mode": "Oidc" } }""", path => Assert.Throws<SettingsFileRefusedException>(() => SettingsFile.Prepare(Shipped(path)))); // its own mode is the mistake
        WithFile("""{ "Auth": { "Mode": "Standalon" } }""", path => Assert.Throws<SettingsFileRefusedException>(() => SettingsFile.Prepare(Shipped(path)))); // a misspelt mode was meant to be one
        WithFile("{ \"Auth\": { \"Mode\": \"Standalone\" ", path => Assert.Throws<SettingsFileRefusedException>(() => SettingsFile.Prepare(Shipped(path)))); // what it holds cannot be told
    }

    [Fact]
    public void AFileLeftOut_IsJustLeftOut_WhenThatLeavesTheAuthenticationAsItIs()
    {
        static bool LeftOut(string path, Dictionary<string, string?>? shipped = null, params string[] environment) =>
            environment.Length == 0 ? SettingsFile.Prepare(Shipped(path, shipped)).Status.Ignored : Prepared(path, environment).File.Status.Ignored;

        WithFile("""{ "Notifications": { "MaxPerHour": "0" } }""", path => Assert.True(LeftOut(path))); // the file names no mode
        WithFile("""{ "Auth": { "Mode": "None" }, "Notifications": { "MaxPerHour": "0" } }""", path => Assert.True(LeftOut(path))); // nor a sign-in mode
        // Without the file the app still asks who is there: the lock-out the fallback exists for (a broken OIDC set-up over a Standalone default).
        WithFile("""{ "Auth": { "Mode": "Oidc" } }""", path => Assert.True(LeftOut(path, new() { ["Auth:Mode"] = "Standalone" })));
        // The mode held above the file, None said there in so many words: the file never decided it.
        WithFile("""{ "Auth": { "Mode": "Standalone" }, "Notifications": { "MaxPerHour": "0" } }""", path => Assert.True(LeftOut(path, null, "--Auth:Mode=None")));
        WithFile("{ not json", path => Assert.True(LeftOut(path, null, "--Auth:Mode=None")));
    }

    [Fact]
    public void TheNextStart_ReadsTheFileAfresh_AndSaysWhenItWouldRefuseIt()
    {
        WithFile("""{ "Defaults": { "Currency": "HUF" } }""", path =>
        {
            var running = Shipped(path);
            var system = new SystemConfiguration(SettingsFile.Prepare(running), running);

            File.WriteAllText(path, """{ "Defaults": { "Currency": "USD" } }""");
            using (var next = system.PrepareNextStart())
                Assert.Equal("USD", next.Configuration!["Defaults:Currency"]); // read afresh, as a restart reads it
            Assert.Equal("HUF", running["Defaults:Currency"]); // what runs stays what the start read

            File.WriteAllText(path, """{ "Auth": { "Mode": "Standalone" }, "Notifications": { "MaxPerHour": "0" } }""");
            using (var next = system.PrepareNextStart())
            {
                Assert.Null(next.Configuration); // the next start would stop: a restart must not be asked for
                Assert.Equal(["Notifications:MaxPerHour must be at least 1."], next.Refusal);
            }
        });
    }

    /// <summary>
    /// The configuration the app starts with before its settings file joins, built by hand: <paramref name="shipped"/> (by default the mode
    /// the shipped appsettings.json sets, None) as the only source, so neither the process's environment nor the real appsettings.json plays a
    /// part, and the file, put after it, has nothing above it.
    /// </summary>
    private static ConfigurationManager Shipped(string path, Dictionary<string, string?>? shipped = null)
    {
        var configuration = new ConfigurationManager();
        var values = new Dictionary<string, string?>(shipped ?? new() { ["Auth:Mode"] = "None" }) { [SettingsFile.PathSetting] = path };
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    /// <summary>
    /// SettingsFile.Prepare on a builder of its own, with <paramref name="environment"/> as command-line arguments: they sit above the file as
    /// the environment does, and touch nothing process-wide.
    /// </summary>
    private static (SettingsFile File, WebApplicationBuilder Builder) Prepared(string path, params string[] environment)
    {
        var builder = WebApplication.CreateBuilder([$"--Settings:Path={path}", .. environment]);
        return (SettingsFile.Prepare(builder), builder);
    }

    private static void WithFile(string json, Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-settings.json");
        File.WriteAllText(path, json);
        try
        {
            test(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool Readable(string path)
    {
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
