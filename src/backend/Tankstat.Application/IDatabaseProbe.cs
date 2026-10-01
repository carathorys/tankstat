namespace Tankstat.Application;

/// <summary>Port implemented by the persistence layer so the application can ask whether the store is reachable.</summary>
public interface IDatabaseProbe
{
    Task<bool> IsReachableAsync(CancellationToken ct);
}
