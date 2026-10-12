namespace Tankstat.Application.SystemSettings;

/// <summary>Restarts the server this request runs on, in its own process (the host stops and is built again), so the settings file is read afresh.</summary>
public interface IServerRestart
{
    void Restart();
}
