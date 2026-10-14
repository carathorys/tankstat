namespace Tankstat.Api.SmokeTests;

/// <summary>
/// The app's own settings file in the real process: it beats the shipped appsettings.json, loses to the environment, and never stops the start.
/// The process gets no environment at all here (<see cref="AppProcess"/> scrubs it), so the file is the only source besides the shipped one.
/// </summary>
public class SettingsFileSmokeTests
{
    private const string Session = "{ session { mode user { email isAdmin } } notices { code message } health { status } }";

    private const string StandaloneFile = """
        {
          "Auth": {
            "Mode": "Standalone",
            "Standalone": { "AdminEmail": "root@example.com", "AdminPassword": "initial-password-1" }
          }
        }
        """;

    private static string WorkDirectory() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"tankstat-smoke-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task TheFile_BeatsTheShippedAppsettings_AndLosesToTheEnvironment()
    {
        var work = WorkDirectory();
        await File.WriteAllTextAsync(Path.Combine(work, "settings.json"), StandaloneFile); // the default place: next to the database

        await using var app = await AppProcess.StartAsync(new Dictionary<string, string?>(), workDirectory: work); // disposed last: it owns the folder
        var data = (await app.Gql(Session)).Data();
        Assert.Equal("STANDALONE", data.GetProperty("session").GetProperty("mode").GetString()); // appsettings.json says None
        Assert.Empty(data.GetProperty("notices").EnumerateArray());
        Assert.True(await app.LogsAsync("Settings file"), app.Log);
        await app.StopAsync();

        await using var overridden = await AppProcess.StartAsync(new Dictionary<string, string?> { ["Auth__Mode"] = "None" }, workDirectory: work);
        Assert.Equal("NONE", (await overridden.Gql(Session)).Data().GetProperty("session").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task TheFile_CanLiveElsewhere()
    {
        var work = WorkDirectory();
        var file = Path.Combine(work, "conf", "tankstat.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, StandaloneFile);

        await using var app = await AppProcess.StartAsync(new Dictionary<string, string?> { ["Settings__Path"] = file }, workDirectory: work);

        Assert.Equal("STANDALONE", (await app.Gql(Session)).Data().GetProperty("session").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task AFileTheAppCouldNotStartWith_IsLeftOut_AndTheAppStarts()
    {
        var work = WorkDirectory();
        // An authentication setting every mode checks, out of range: a mistake that would stop the start. The mode comes from the test's own
        // environment, never from what appsettings.json happens to set (and a file whose loss would turn authentication off is refused
        // instead of left out: SettingsFileTests).
        await File.WriteAllTextAsync(Path.Combine(work, "settings.json"), """{ "Auth": { "AccessTokenMinutes": "0" } }""");

        await using var app = await AppProcess.StartAsync(new Dictionary<string, string?> { ["Auth__Mode"] = "None" }, workDirectory: work);

        var data = (await app.Gql(Session)).Data();
        Assert.Equal("NONE", data.GetProperty("session").GetProperty("mode").GetString());
        var codes = data.GetProperty("notices").EnumerateArray().Select(n => n.GetProperty("code").GetString()).ToList();
        Assert.Equal(["AUTH_DISABLED", "SETTINGS_FILE_IGNORED", "SETUP_PENDING"], codes); // a new instance waits for its setup: that notice comes last
        Assert.True(await app.LogsAsync("left out"), app.Log);
        Assert.Contains("Auth:AccessTokenMinutes", app.Log);
        Assert.Contains("fail: ", app.Log); // an Error line, as the console writes it
    }

    [Fact]
    public async Task TheEnvironment_StillStopsTheApp_WhenItIsWrong_WhateverTheFileSays()
    {
        var work = WorkDirectory();
        await File.WriteAllTextAsync(Path.Combine(work, "settings.json"), StandaloneFile); // read by the process: its working directory, the default place

        var (exitCode, log) = await AppProcess.RunUntilExitAsync(new Dictionary<string, string?> { ["Auth__Mode"] = "Kerberos" }, workDirectory: work); // removes the folder

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Auth:Mode", log, StringComparison.OrdinalIgnoreCase);
    }
}
