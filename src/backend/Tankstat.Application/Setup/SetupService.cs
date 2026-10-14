using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.SystemSettings;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Instance;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Setup;

/// <summary>Whether the setup waits: public (the setup page and its notice), nothing secret in it.</summary>
public sealed record SetupInfo(bool Pending);

/// <summary>What the setup's last step did: the administrator it made (Standalone), and whether the server restarts now for the settings saved.</summary>
public sealed record SetupFinished(User? Administrator, bool Restarting);

/// <summary>
/// The setup of a new instance. A new instance waits until it has its gatekeeper: its first administrator (Standalone, OIDC, proxy), or
/// with sign-in off the acknowledgement that everyone who reaches it may see and change everything. While it waits, the one-time code
/// from the server's log (<see cref="OneTimeCode"/>) opens the setup page's steps (<see cref="SetupGate"/>), since no administrator exists
/// to ask: the settings (the wizard saves them through <see cref="SystemSettingsService"/> with the code), then <see cref="FinishAsync"/>;
/// once it is completed nothing here opens again. What the environment already says completes it at start (<see cref="StartAsync"/>), so
/// an unattended deployment never sees the page; a restore brings the backup's setup state along.
/// </summary>
public sealed class SetupService(
    ISetupStore store,
    SetupStatus status,
    SetupGate gate,
    OneTimeCode code,
    AuthService authService,
    AccessService access,
    SystemSettingsService settings,
    ISystemConfiguration configuration,
    IServerRestart restart,
    IUserRepository users,
    IBackupStore backups,
    IDatabaseFacts database,
    IOptionsSnapshot<BackupOptions> backupOptions,
    IOptions<AuthOptions> auth,
    IOptions<SetupOptions> options,
    TimeProvider clock,
    ILogger<SetupService> logger)
{
    /// <summary>
    /// At every start, after a waiting restore and the migrations: decides a setup that is undecided (a database from before setup existed
    /// is set up when it holds data, a new one waits; decided before the bootstrap administrator is made, which would count as data), then
    /// completes a waiting one the environment completes (<c>Setup:Enabled=false</c>, the bootstrap administrator, <c>Auth:AdminEmails</c>).
    /// A set-up Standalone instance without an administrator still needs the bootstrap credentials: the start stops without them, as it
    /// always did; only a waiting one is set up in the browser. While it waits, the code is logged.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var phase = await store.PhaseAsync(ct);
        if (phase == SetupPhase.Undecided)
        {
            phase = await store.DecideAsync(await store.HoldsDataAsync(ct), clock.GetUtcNow(), ct);
            if (phase == SetupPhase.Completed) logger.LogInformation("The database holds data from before the setup page existed: the instance counts as set up");
        }
        var administratorSeenTo = false; // CompletedByAsync made sure of the administrator (or stopped the start) already
        if (phase == SetupPhase.Pending && await CompletedByAsync(ct) is { } reason)
        {
            // Completed either way: where this write finds it completed, another server starting at the same moment was first.
            if (await store.CompleteAsync(SetupCompletion.Environment, clock.GetUtcNow(), ct))
                logger.LogInformation("The setup is completed by the environment: {Reason}", reason);
            phase = SetupPhase.Completed;
            administratorSeenTo = true;
        }
        if (phase != SetupPhase.Pending && !administratorSeenTo) await authService.BootstrapAdminAsync(setupWaits: false, ct);

        status.Set(phase == SetupPhase.Pending);
        // A Warning, so a log kept to warnings still has it: without it the instance cannot be set up from the browser.
        if (phase == SetupPhase.Pending)
            logger.LogWarning("Setup is waiting: open /setup in the browser and enter the one-time setup code {Code}", code.Value);
    }

    /// <summary>
    /// What in the environment completes a waiting setup, by the setting's name; null when nothing does. Whatever can still stop the start
    /// runs before the completion is written: with the setup turned off, a Standalone instance without an administrator or the credentials
    /// for one stops here, and a later start with the setup on still finds it waiting.
    /// </summary>
    private async Task<string?> CompletedByAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            await authService.BootstrapAdminAsync(setupWaits: false, ct);
            return "Setup:Enabled is false";
        }
        switch (auth.Value.Mode)
        {
            case AuthMode.Standalone:
                if (await users.AnyLocalAdminAsync(ct)) return "a local administrator exists";
                if (await authService.BootstrapAdminAsync(setupWaits: true, ct)) return "Auth:Standalone:AdminEmail";
                // false is "no credentials given", or another server starting at the same moment made the administrator first
                return await users.AnyLocalAdminAsync(ct) ? "a local administrator exists" : null;
            case AuthMode.Oidc or AuthMode.ProxyHeader:
                return auth.Value.AdminEmails.Any(e => !string.IsNullOrWhiteSpace(e)) ? "Auth:AdminEmails" : null;
            default:
                return null;
        }
    }

    /// <summary>Whether the setup waits, asked of the database while this process thinks so (another server may have completed it).</summary>
    public async Task<SetupInfo> InfoAsync(CancellationToken ct) => new(await gate.WaitsAsync(ct));

    /// <summary>The restore points of the backups folder, to restore one into the waiting instance; with the recipient new backups are encrypted for.</summary>
    public async Task<(RestorePoints Points, string? CurrentRecipient)> RestorePointsAsync(string? typedCode, CancellationToken ct)
    {
        await gate.RequireAsync(typedCode, ct);
        var points = new RestorePoints(backups.List(), await database.LastMigrationAsync(ct), await database.KnownMigrationsAsync(ct));
        return (points, backupOptions.Value.CurrentRecipient);
    }

    /// <summary>The largest backup the setup page may upload (`Backups:MaxUploadMb`), which the Backups page tells administrators only.</summary>
    public async Task<int> UploadLimitMbAsync(string? typedCode, CancellationToken ct)
    {
        await gate.RequireAsync(typedCode, ct);
        return backupOptions.Value.MaxUploadMb;
    }

    /// <summary>
    /// The setup's last step, once the wizard saved its settings: the gatekeeper of the sign-in mode the next start runs, then the one restart
    /// the saved settings wait for. Standalone makes the first administrator (the bootstrap administrator's rules: a valid address, a password
    /// within the policy) and completes the setup with it, in one transaction; sign-in off needs the acknowledgement that everyone who reaches
    /// the instance may see and change everything; OIDC and proxy complete nothing here: the administrators the settings name complete it as
    /// the server starts, else the signed-in user claims it after the restart (<see cref="ClaimAdministratorAsync"/>, the same code). What the
    /// next start would refuse refuses this step before anything is completed.
    /// </summary>
    public async Task<SetupFinished> FinishAsync(string? typedCode, string? email, string? password, bool acknowledgeOpenAccess, CancellationToken ct)
    {
        await gate.RequireAsync(typedCode, ct);
        var restarting = await settings.RestartPendingAsync(ct);
        if (restarting) await settings.RequireStartableAsync(ct);
        User? administrator = null;
        switch (NextMode())
        {
            case AuthMode.Standalone:
                if (await users.AnyLocalAdminAsync(ct)) throw new DomainException("setup.administratorExists", "An administrator exists already: sign in.");
                administrator = authService.NewAdministrator(email, password);
                if (await users.FindLocalByEmailAsync(administrator.Email, ct) is not null) throw new DomainException("user.emailExists", "A user with this e-mail already exists.");
                AfterCompletion(await store.CompleteWithAdministratorAsync(administrator, clock.GetUtcNow(), ct));
                logger.LogInformation("The setup page made the first administrator {UserId}: the setup is completed", administrator.Id);
                break;
            case AuthMode.None:
                if (!acknowledgeOpenAccess)
                    throw new DomainException("setup.acknowledgementRequired", "With sign-in off, the setup ends with the acknowledgement that everyone who reaches the instance sees and changes everything.");
                AfterCompletion(await store.CompleteAsync(SetupCompletion.Acknowledged, clock.GetUtcNow(), ct));
                logger.LogInformation("Open access was acknowledged on the setup page (sign-in is off): the setup is completed");
                break;
        }
        if (restarting)
        {
            logger.LogInformation("The setup page restarts the server for the settings it saved");
            restart.Restart();
        }
        return new SetupFinished(administrator, restarting);
    }

    /// <summary>The sign-in mode the next start runs: what the saved settings say beside the environment's (the running one waits for a restart).</summary>
    private AuthMode NextMode() => Enum.TryParse<AuthMode>(configuration.Running["Auth:Mode"], ignoreCase: true, out var mode) ? mode : AuthMode.None;

    /// <summary>
    /// OIDC and proxy mode: the signed-in user (provisioned by their sign-in, so anyone the provider or proxy lets in) becomes the first
    /// administrator, which completes the setup; the code is the whole guard, since no administrator exists to ask.
    /// </summary>
    public async Task<Principal> ClaimAdministratorAsync(string? typedCode, CancellationToken ct)
    {
        await gate.RequireAsync(typedCode, ct);
        RequireMode(AuthMode.Oidc, AuthMode.ProxyHeader);
        var user = await access.RequirePrincipalAsync(ct);
        AfterCompletion(await store.CompleteWithClaimAsync(user.Id, clock.GetUtcNow(), ct));
        logger.LogInformation("User {UserId} became the first administrator on the setup page: the setup is completed", user.Id);
        return user with { IsAdmin = true };
    }

    private void RequireMode(params AuthMode[] modes)
    {
        if (!modes.Contains(auth.Value.Mode))
            throw new DomainException("setup.otherMode", $"This step is not part of the setup in {auth.Value.Mode} sign-in mode.", new { Mode = auth.Value.Mode.ToString() });
    }

    /// <summary>
    /// After a step's conditional completion: the setup is completed either way, so this process's copy follows; when the write found it
    /// completed, another request (or server) was first and the step is refused.
    /// </summary>
    private void AfterCompletion(bool completedHere)
    {
        status.Set(false);
        if (!completedHere) throw SetupGate.Completed();
    }
}
