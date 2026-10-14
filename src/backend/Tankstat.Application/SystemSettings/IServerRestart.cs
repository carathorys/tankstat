namespace Tankstat.Application.SystemSettings;

/// <summary>Restarts the server this request runs on, in its own process (the host stops and is built again), so the settings file is read afresh.</summary>
public interface IServerRestart
{
    void Restart();

    /// <summary>
    /// From a start step, for a settings file it just wrote (a restore's): asks for the restart and ends this start right here with
    /// <see cref="ServerRestartException"/>, so no later step runs, and Kestrel never listens, under the settings the file replaced; the
    /// next host reads it. <see cref="Restart"/> stops a moment later instead, which only suits a request (its answer must get out), and in
    /// the middle of a start would cut it short with a crash.
    /// </summary>
    void RestartNow();
}

/// <summary>Ends a start that asked for the next one at once (<see cref="IServerRestart.RestartNow"/>); Program.cs builds that one.</summary>
public sealed class ServerRestartException() : Exception("The server restarts at once: a start step changed the settings file.");
