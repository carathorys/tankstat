using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Instance;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Setup;

/// <summary>Whether the setup waits: public (the setup page and its notice), nothing secret in it.</summary>
public sealed record SetupInfo(bool Pending);

/// <summary>
/// The setup of a new instance. A new instance waits until it has its gatekeeper: its first administrator (Standalone, OIDC, proxy), or
/// with sign-in off the acknowledgement that everyone who reaches it may see and change everything. While it waits, the one-time code
/// from the server's log (<see cref="OneTimeCode"/>) opens the setup page's steps, since no administrator exists to ask; once it is
/// completed nothing here opens again. What the environment already says completes it at start (<see cref="StartAsync"/>), so an
/// unattended deployment never sees the page; a restore brings the backup's setup state along.
/// </summary>
public sealed class SetupService(
    ISetupStore store,
    SetupStatus status,
    OneTimeCode code,
    AuthService authService,
    AccessService access,
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
                return await authService.BootstrapAdminAsync(setupWaits: true, ct) ? "Auth:Standalone:AdminEmail" : null;
            case AuthMode.Oidc or AuthMode.ProxyHeader:
                return auth.Value.AdminEmails.Any(e => !string.IsNullOrWhiteSpace(e)) ? "Auth:AdminEmails" : null;
            default:
                return null;
        }
    }

    /// <summary>Whether the setup waits, asked of the database while this process thinks so (another server may have completed it).</summary>
    public async Task<SetupInfo> InfoAsync(CancellationToken ct) => new(await status.PendingAsync(store, ct));

    /// <summary>The restore points of the backups folder, to restore one into the waiting instance; with the recipient new backups are encrypted for.</summary>
    public async Task<(RestorePoints Points, string? CurrentRecipient)> RestorePointsAsync(string? typedCode, CancellationToken ct)
    {
        await RequireCodeAsync(typedCode, ct);
        var points = new RestorePoints(backups.List(), await database.LastMigrationAsync(ct), await database.KnownMigrationsAsync(ct));
        return (points, backupOptions.Value.CurrentRecipient);
    }

    /// <summary>
    /// Standalone: makes the first administrator (the bootstrap administrator's rules: a valid address, a password within the policy) and
    /// completes the setup with it, in one transaction; the caller signs the new administrator in.
    /// </summary>
    public async Task<User> CreateAdministratorAsync(string? typedCode, string? email, string? password, CancellationToken ct)
    {
        await RequireCodeAsync(typedCode, ct);
        RequireMode(AuthMode.Standalone);
        if (await users.AnyLocalAdminAsync(ct)) throw new DomainException("setup.administratorExists", "An administrator exists already: sign in.");
        var admin = authService.NewAdministrator(email, password);
        if (await users.FindLocalByEmailAsync(admin.Email, ct) is not null) throw new DomainException("user.emailExists", "A user with this e-mail already exists.");
        AfterCompletion(await store.CompleteWithAdministratorAsync(admin, clock.GetUtcNow(), ct));
        logger.LogInformation("The setup page made the first administrator {UserId}: the setup is completed", admin.Id);
        return admin;
    }

    /// <summary>
    /// OIDC and proxy mode: the signed-in user (provisioned by their sign-in, so anyone the provider or proxy lets in) becomes the first
    /// administrator, which completes the setup; the code is the whole guard, since no administrator exists to ask.
    /// </summary>
    public async Task<Principal> ClaimAdministratorAsync(string? typedCode, CancellationToken ct)
    {
        await RequireCodeAsync(typedCode, ct);
        RequireMode(AuthMode.Oidc, AuthMode.ProxyHeader);
        var user = await access.RequirePrincipalAsync(ct);
        AfterCompletion(await store.CompleteWithClaimAsync(user.Id, clock.GetUtcNow(), ct));
        logger.LogInformation("User {UserId} became the first administrator on the setup page: the setup is completed", user.Id);
        return user with { IsAdmin = true };
    }

    /// <summary>With sign-in off: the acknowledgement that everyone who reaches the instance may see and change everything completes the setup.</summary>
    public async Task<SetupInfo> AcknowledgeOpenAccessAsync(string? typedCode, CancellationToken ct)
    {
        await RequireCodeAsync(typedCode, ct);
        RequireMode(AuthMode.None);
        AfterCompletion(await store.CompleteAsync(SetupCompletion.Acknowledged, clock.GetUtcNow(), ct));
        logger.LogInformation("Open access was acknowledged on the setup page (sign-in is off): the setup is completed");
        return new SetupInfo(false);
    }

    /// <summary>
    /// The setup waits (asked of the database: another server may have completed it) and the code matches; a wrong code is refused (the
    /// GraphQL listener logs the refusal, never what was typed).
    /// </summary>
    private async Task RequireCodeAsync(string? typedCode, CancellationToken ct)
    {
        if (!await status.PendingAsync(store, ct)) throw Completed();
        if (!code.Matches(typedCode)) throw new ForbiddenException("setup.codeInvalid", "The one-time setup code from the server's log is needed.");
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
        if (!completedHere) throw Completed();
    }

    private static DomainException Completed() => new("setup.completed", "The setup is completed already.");
}
