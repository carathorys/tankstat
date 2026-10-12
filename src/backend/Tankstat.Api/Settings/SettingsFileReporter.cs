using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>
/// Says at start what became of the settings file. It runs as the first hosted service because the decision is made before the host exists,
/// where there is no logger yet: a file that was left out is an Error naming the settings at fault (never their values), one in use is noted.
/// </summary>
internal sealed class SettingsFileReporter(SettingsFileStatus status, ILogger<SettingsFileReporter> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        if (status.Ignored)
            logger.LogError("The settings file {Path} was left out because the app could not start with it; it runs without the file until the next start: {Problems}",
                status.Path, string.Join(" ", status.Problems));
        else if (status.Exists)
            logger.LogInformation("Settings file {Path} in use (environment variables and the command line still win)", status.Path);
        else
            logger.LogDebug("No settings file at {Path}", status.Path);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
