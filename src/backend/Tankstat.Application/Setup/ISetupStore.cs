using Tankstat.Domain.Instance;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Setup;

/// <summary>
/// The setup state in the instance's one row (<see cref="InstanceState"/>), written only by conditional updates, so two servers starting
/// at once, or two requests of the setup page, decide or complete it once.
/// </summary>
public interface ISetupStore
{
    /// <summary>Where the setup stands; <see cref="SetupPhase.Undecided"/> when there is no row either.</summary>
    Task<SetupPhase> PhaseAsync(CancellationToken ct);

    /// <summary>Whether the database holds what only a used instance has: any user, any vehicle (the trash included).</summary>
    Task<bool> HoldsDataAsync(CancellationToken ct);

    /// <summary>
    /// Decides an undecided setup: completed (<see cref="SetupCompletion.Upgrade"/>) when the database held data, else pending; where another
    /// start decided first, that decision stands. Returns the phase it ends in.
    /// </summary>
    Task<SetupPhase> DecideAsync(bool holdsData, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Reopens a completed setup (a set-up Standalone instance that has no administrator, #149): pending again from <paramref name="now"/>,
    /// how it was completed kept (<see cref="CompleteReopenedAsync"/>); false when it was not completed (another start reopened it first).
    /// </summary>
    Task<bool> ReopenAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Completes a reopened setup again as it was completed before (a start in another sign-in mode than Standalone: the way back is the
    /// environment); false when the setup waits as a new one does, with no completion before it, or is not pending.
    /// </summary>
    Task<bool> CompleteReopenedAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Completes a pending setup; false when it was not pending (completed meanwhile).</summary>
    Task<bool> CompleteAsync(SetupCompletion via, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Adds the first administrator and completes the pending setup (<see cref="SetupCompletion.Administrator"/>) in one transaction; false,
    /// with nothing added, when the setup was not pending.
    /// </summary>
    Task<bool> CompleteWithAdministratorAsync(User administrator, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Makes the signed-in user (OIDC, proxy) the first administrator and completes the pending setup
    /// (<see cref="SetupCompletion.Administrator"/>) in one transaction; false, with nobody promoted, when the setup was not pending.
    /// </summary>
    Task<bool> CompleteWithClaimAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}
