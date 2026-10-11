using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>The flag Program.cs reads once the host stopped: build and run again, or let the process end.</summary>
public sealed class RestartRequests
{
    public bool Requested { get; private set; }

    public void Request() => Requested = true;

    public void Reset() => Requested = false;
}

/// <summary>
/// Restarts in the process: the host is asked to stop a moment later (so the answer to the request that asked gets out), and Program.cs
/// builds and runs a new one. Never by exiting: Docker's default restart policy leaves a container that exited cleanly stopped.
/// </summary>
internal sealed class ServerRestart(RestartRequests requests, IHostApplicationLifetime lifetime, ILogger<ServerRestart> logger) : IServerRestart
{
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

    public void Restart()
    {
        requests.Request();
        logger.LogInformation("Restarting the server: the host stops and is started again in this process");
        _ = Task.Run(async () =>
        {
            await Task.Delay(Grace);
            lifetime.StopApplication();
        });
    }
}
