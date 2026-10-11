namespace Tankstat.Api.SmokeTests;

/// <summary>An administrator's restart in the real process: the host stops and starts again in the same process, on the same port, and what was saved into the settings file counts afterwards.</summary>
public class RestartSmokeTests
{
    private static readonly Dictionary<string, string?> Standalone = new()
    {
        ["Auth__Mode"] = "Standalone",
        ["Auth__Standalone__AdminEmail"] = "root@example.com",
        ["Auth__Standalone__AdminPassword"] = "initial-password-1",
    };

    private const string Session = "{ session { mode user { email } } vehicleDefaults { currency } }";

    /// <summary>Waits until the app answers again after a restart and the answer passes <paramref name="ready"/>.</summary>
    private static async Task<System.Text.Json.JsonElement> WhenBackAsync(AppProcess app, Func<System.Text.Json.JsonElement, bool> ready, IDictionary<string, string>? headers = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(app.HasExited, "the process must not exit for a restart:\n" + app.Log);
            try
            {
                var data = (await app.Gql(Session, headers: headers)).Data();
                if (ready(data)) return data;
            }
            catch (Exception) when (!app.HasExited)
            {
                // not back yet, or the old host still answering
            }
            await Task.Delay(200);
        }
        throw new TimeoutException("The app did not come back after the restart:\n" + app.Log);
    }

    [Fact]
    public async Task RestartServer_StartsTheHostAgain_InTheSameProcess_AndWhatWasSavedCounts()
    {
        await using var app = await AppProcess.StartAsync(Standalone);
        await app.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "root@example.com", password = "initial-password-1" } });
        var signedIn = new Dictionary<string, string> { ["Cookie"] = app.CookieHeader };
        var saved = (await app.Gql("mutation { updateSystemSettings(changes: [{ key: \"Defaults:Currency\", value: \"HUF\" }]) { restartPending } }", headers: signedIn)).Data();
        Assert.True(saved.GetProperty("updateSystemSettings").GetProperty("restartPending").GetBoolean());
        Assert.Equal("EUR", (await app.Gql(Session)).Data().GetProperty("vehicleDefaults").GetProperty("currency").GetString());
        var file = Path.Combine(app.WorkDirectory, "settings.json");
        Assert.True(File.Exists(file), "the settings file lands in the working directory by default");
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));

        Assert.True((await app.Gql("mutation { restartServer }", headers: signedIn)).Data().GetProperty("restartServer").GetBoolean());

        var back = await WhenBackAsync(app, d => d.GetProperty("vehicleDefaults").GetProperty("currency").GetString() == "HUF", signedIn);
        Assert.False(app.HasExited);
        Assert.Equal("root@example.com", back.GetProperty("session").GetProperty("user").GetProperty("email").GetString()); // the same mode: the sign-in survives the restart
        Assert.Contains("Restarting the server", app.Log);
        Assert.Contains("Settings file", app.Log); // the second host read the file the first one wrote (the first start had none: a Debug line, unseen in Production)
        Assert.Contains("database migrations", app.Log); // the first start migrated...
        Assert.Contains("up to date", app.Log); // ...and the second found the database ready: two starts in one process
    }
}
