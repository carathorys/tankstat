using Tankstat.Application.Auth;

namespace Tankstat.Application.Runs;

/// <summary>
/// The restore context (#340): the ways into the services that only a partial restore takes, where a row keeps the backup's id, a car
/// goes to its owner and a chart or a dashboard to the person whose it was. The principal stays the administrator who runs it (access is
/// judged as theirs, the log names them, the notifier leaves them out); only these ways in let them choose otherwise.
/// </summary>
public static class RunRestore
{
    /// <summary>Inside a partial restore's run.</summary>
    public static bool Active => RunContext.Current is { Restoring: true };

    /// <summary>
    /// Refuses a restore-only way in outside a partial restore's run, or for anyone but a real administrator. A bug, never a person's
    /// doing: nothing outside the restore's work reaches these ways in.
    /// </summary>
    public static void Require(Principal principal)
    {
        if (!Active || !principal.IsAdmin || principal.IsAnonymous)
            throw new InvalidOperationException("Only a partial restore's run, started by an administrator, may choose ids, owners and creators.");
    }
}
