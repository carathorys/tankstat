using Tankstat.Application.Auth;

namespace Tankstat.Application.Runs;

/// <summary>
/// The run this code is part of (#335), if any: set by <see cref="IRunScope"/> around a run's work and carried by the execution context,
/// so only what the work itself calls sees it. A request, a hosted service and anything else started outside the work never do.
/// </summary>
public sealed class RunContext
{
    private static readonly AsyncLocal<RunContext?> Ambient = new();

    public RunContext(Principal starter, IReadOnlyCollection<Guid> vehicleIds)
    {
        Starter = starter;
        VehicleIds = vehicleIds;
    }

    /// <summary>Who the services act as inside the run.</summary>
    public Principal Starter { get; }

    public IReadOnlyCollection<Guid> VehicleIds { get; }

    public static RunContext? Current => Ambient.Value;

    /// <summary>Sets the run for the caller's execution context and what it calls; the returned value puts back what was there.</summary>
    public static IDisposable Enter(RunContext run)
    {
        var before = Ambient.Value;
        Ambient.Value = run;
        return new Leave(before);
    }

    private sealed class Leave(RunContext? before) : IDisposable
    {
        public void Dispose() => Ambient.Value = before;
    }
}
