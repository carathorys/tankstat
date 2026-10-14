using System.Runtime.InteropServices;
using Tankstat.Application.Setup;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>
/// The flag Program.cs reads once the host stopped: build and run again, or let the process end. A stop signal ends the process even when a
/// restart was asked for a moment before (<see cref="EndOnStopSignals"/>).
/// </summary>
public sealed class RestartRequests
{
    private volatile bool _requested;
    private volatile bool _stopping;

    /// <summary>
    /// The instance's one-time code for this process: a restart in it keeps it (the setup page that asked for the restart goes on with the
    /// code it was given, a provider's sign-in after it too); a new process makes a new one.
    /// </summary>
    public OneTimeCode Code { get; } = new();

    /// <summary>A restart was asked for since this host started, and no stop signal came since the process started.</summary>
    public bool Requested => _requested && !_stopping;

    public void Request() => _requested = true;

    public void Reset() => _requested = false;

    /// <summary>
    /// Listens, until disposed, for the signals that stop the app for good (SIGTERM from <c>docker stop</c>, SIGINT from Ctrl+C, SIGQUIT). The
    /// host's own handler still stops the host gracefully and keeps the process alive meanwhile; this one only marks the process as stopping,
    /// so a signal that came while the host was stopping for a restart is not spent on that stop and followed by a new host (the stop would
    /// then wait for the orchestrator's kill). Between two hosts no handler keeps the process alive, and the signal ends it as it always did.
    /// </summary>
    public IDisposable EndOnStopSignals()
    {
        var registrations = new List<PosixSignalRegistration>();
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS() && !OperatingSystem.IsBrowser()) // where the host listens too
        {
            foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM })
                registrations.Add(PosixSignalRegistration.Create(signal, _ => _stopping = true));
        }
        return new Registrations(registrations);
    }

    private sealed class Registrations(List<PosixSignalRegistration> registrations) : IDisposable
    {
        public void Dispose() => registrations.ForEach(r => r.Dispose());
    }
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

    public void RestartNow()
    {
        requests.Request();
        logger.LogInformation("Restarting the server at once: this start wrote the settings file, which the next one reads");
        throw new ServerRestartException();
    }
}
