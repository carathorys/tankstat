using Tankstat.Domain.Instance;

namespace Tankstat.Application.Setup;

/// <summary>
/// Whether this instance's setup waits, as its start decided (<see cref="SetupService.StartAsync"/>) and the setup page's completion
/// changed since: what the public notice says without asking the database. The database decides every completion (a conditional write,
/// so two at once never both complete); this is its copy in the process. Several servers on one database each keep their own (like the
/// one-time code): what the setup opens asks the database through <see cref="PendingAsync"/>, so a completion on another server closes it
/// here too, and this process learns of it then (or at its next start).
/// </summary>
public sealed class SetupStatus
{
    private volatile bool _pending;

    public bool Pending => _pending;

    public void Set(bool pending) => _pending = pending;

    /// <summary>
    /// Whether the setup still waits: asked of the database while this process thinks so, since another server may have completed it; a
    /// completion found there is kept, so the notice goes too.
    /// </summary>
    public async Task<bool> PendingAsync(ISetupStore store, CancellationToken ct)
    {
        if (!_pending) return false;
        if (await store.PhaseAsync(ct) == SetupPhase.Pending) return true;
        _pending = false;
        return false;
    }
}
