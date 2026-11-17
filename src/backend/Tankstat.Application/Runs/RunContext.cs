using Tankstat.Application.Auth;

namespace Tankstat.Application.Runs;

/// <summary>
/// The run this code is part of (#335), if any: set by <see cref="IRunScope"/> around a run's work and carried by the execution context,
/// so only what the work itself calls sees it. A request, a hosted service and anything else started outside the work never do.
/// </summary>
public sealed class RunContext
{
    private static readonly AsyncLocal<RunContext?> Ambient = new();

    public RunContext(Principal starter, IReadOnlyCollection<Guid> vehicleIds, IRun? run = null, bool restoring = false)
    {
        Starter = starter;
        VehicleIds = vehicleIds;
        Run = run;
        Restoring = restoring;
    }

    /// <summary>The run is a partial restore (#340): what it adds keeps the backup's ids, owners and creators (<see cref="RunRestore"/>).</summary>
    public bool Restoring { get; }

    /// <summary>
    /// The run itself (#339), for what outside the database a service does inside it: a file written goes when its savepoint is rolled
    /// back (<see cref="IRun.OnRollback"/>), a file deleted waits for the commit (<see cref="IRun.AfterCommit"/>).
    /// </summary>
    public IRun? Run { get; }

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
