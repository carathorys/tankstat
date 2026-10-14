namespace Tankstat.Application.Setup;

/// <summary>
/// Whether this instance's setup waits, as its start decided (<see cref="SetupService.StartAsync"/>) and the setup page's completion
/// changed since: what the public notice and the setup query say without asking the database. The database decides every completion
/// (a conditional write, so two at once never both complete); this is its copy in the process. Several servers on one database each keep
/// their own (like the one-time code), so another server learns of a completion at its next start.
/// </summary>
public sealed class SetupStatus
{
    private volatile bool _pending;

    public bool Pending => _pending;

    public void Set(bool pending) => _pending = pending;
}
