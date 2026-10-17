using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>Sign-in related use cases for every mode: local login/password handling and external user provisioning.</summary>
public sealed class AuthService(
    IUserRepository users, IPasswordHasher hasher, PasswordResetService resets, IPasswordResetQueue resetQueue, UserSessionService sessions,
    AccessService access, IOptions<AuthOptions> auth, PasswordRequirements passwords, TimeProvider clock, ILogger<AuthService> logger)
{
    /// <summary>The error key of a refused sign-in of a disabled user; the OIDC failure handler recognises it.</summary>
    public const string AccountDisabledKey = "auth.accountDisabled";

    /// <summary>One new external user is created by one request at a time within this process (an instance per request, so static).</summary>
    private static readonly StripedLocks Creating = new();

    // Verified against when the account does not exist, so response time does not reveal which e-mails are registered.
    private readonly Lazy<string> _decoyHash = new(() => hasher.Hash("decoy-password-for-timing"));

    private void RequireMode(AuthMode mode)
    {
        if (auth.Value.Mode != mode) throw new DomainException("auth.modeUnavailable", $"This is not available in {auth.Value.Mode} authentication mode.", new { Mode = auth.Value.Mode.ToString() });
    }

    public async Task<User> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        RequireMode(AuthMode.Standalone);
        var options = auth.Value.Standalone;
        var now = clock.GetUtcNow();

        var user = await users.FindLocalByEmailAsync(User.NormalizeEmailOrEmpty(email), ct);
        if (user?.PasswordHash is null || user.IsDisabled || user.IsLockedOut(now))
        {
            hasher.Verify(_decoyHash.Value, password ?? "");
            // Never the typed e-mail: who tried is only named when the account exists, by its id.
            if (user is null) logger.LogWarning("Sign-in failed: unknown account");
            else logger.LogWarning("Sign-in failed for user {UserId}: {Reason}", user.Id, user.PasswordHash is null ? "no password" : user.IsDisabled ? "account disabled" : "locked out");
            throw new InvalidCredentialsException();
        }

        if (!hasher.Verify(user.PasswordHash, password ?? ""))
        {
            user.RegisterFailedLogin(now, options.MaxFailedAttempts, TimeSpan.FromMinutes(options.LockoutMinutes));
            await users.UpdateAsync(user, ct);
            logger.LogWarning("Sign-in failed for user {UserId}: wrong password (locked out: {LockedOut})", user.Id, user.IsLockedOut(now));
            throw new InvalidCredentialsException();
        }

        // A password that met the rules of its time still signs in; one short of today's minimum is asked to change (the app does).
        user.RegisterSuccessfulLogin(passwords.Meets(password ?? ""));
        await users.UpdateAsync(user, ct);
        logger.LogInformation("User {UserId} signed in", user.Id);
        if (user.PasswordChangeRequired) logger.LogInformation("User {UserId} is asked to change their password: it is shorter than the minimum now", user.Id);
        return user;
    }

    /// <summary>
    /// Changes the signed-in user's password; the returned user carries the new session version. Every other device is signed out; the
    /// one that asked (<paramref name="currentSessionId"/>) stays signed in. The new password must come twice (<see cref="PasswordPolicy.RequireRepeated"/>),
    /// checked before the current one is.
    /// </summary>
    public async Task<User> ChangePasswordAsync(
        string? currentPassword, string? newPassword, string? newPasswordConfirmation, CancellationToken ct, Guid? currentSessionId = null)
    {
        RequireMode(AuthMode.Standalone);
        var principal = await access.RequirePrincipalAsync(ct);
        PasswordPolicy.RequireRepeated(newPassword, newPasswordConfirmation);
        var user = await users.FindByIdAsync(principal.Id, ct) ?? throw new UnauthenticatedException();

        if (user.PasswordHash is null || !hasher.Verify(user.PasswordHash, currentPassword ?? ""))
        {
            logger.LogWarning("Password change refused for user {UserId}: the current password is incorrect", user.Id);
            throw new DomainException("password.currentIncorrect", "The current password is not correct.");
        }
        passwords.Validate(newPassword);

        user.SetPasswordHash(hasher.Hash(newPassword!));
        await users.UpdateAsync(user, ct);
        await resets.RevokeAllAsync(user.Id, ct);
        // First this device takes the new version, then the others end: its next refresh must not find a stale version.
        if (currentSessionId is { } keep) await sessions.AdoptVersionAsync(keep, user, ct);
        await sessions.RevokeOthersAsync(user.Id, currentSessionId, ct);
        logger.LogInformation("User {UserId} changed their password", user.Id);
        return user;
    }

    /// <summary>
    /// Always succeeds from the caller's view, whether or not the e-mail is registered, with the same work for every address: one lookup,
    /// and for a known account a request queued for <see cref="PasswordResetService.SendRequestedAsync"/>, which e-mails a link after the
    /// answer when SMTP is set up and the account's latest link is older than the cool-down (or expired), so the endpoint cannot be used to
    /// flood a mailbox. Neither how long the answer takes nor a mail server's failure tells registered addresses apart.
    /// </summary>
    public async Task RequestPasswordResetAsync(string? email, CancellationToken ct)
    {
        RequireMode(AuthMode.Standalone);
        var user = await users.FindLocalByEmailAsync(User.NormalizeEmailOrEmpty(email), ct);
        if (user is null || user.IsDisabled)
        {
            logger.LogInformation("A password reset was requested for an unknown or disabled account");
            return;
        }
        // Answered the same way as every other request, so the caller learns nothing; only the log says why nothing went out.
        if (!resets.CanEmail)
        {
            // A link nobody receives would only replace one an administrator handed over.
            logger.LogInformation("User {UserId} requested a password reset, but no e-mail can be sent; nothing was issued", user.Id);
            return;
        }
        // With the session version as it is now: a new password before the request is served revokes every link, so it sends nothing then.
        switch (resetQueue.Enqueue(user.Id, user.SessionVersion))
        {
            case ResetRequestQueueing.Queued:
                logger.LogInformation("User {UserId} requested a password reset; the e-mail goes out after the answer", user.Id);
                break;
            case ResetRequestQueueing.AlreadyWaiting:
                logger.LogInformation("User {UserId} requested a password reset while an earlier request was still waiting to be sent", user.Id);
                break;
            case ResetRequestQueueing.QueueFull:
                logger.LogWarning("User {UserId} requested a password reset, but too many requests are waiting to be sent; nothing was queued", user.Id);
                break;
        }
    }

    /// <summary>Sets a new password through a reset link; the new password must come twice, checked before the link is looked up.</summary>
    public async Task<User> ResetPasswordAsync(string? token, string? newPassword, string? newPasswordConfirmation, CancellationToken ct)
    {
        RequireMode(AuthMode.Standalone);
        PasswordPolicy.RequireRepeated(newPassword, newPasswordConfirmation);
        var (user, record) = await resets.ResolveAsync(token, ct);
        passwords.Validate(newPassword);

        await resets.MarkUsedAsync(record, ct);
        user.SetPasswordHash(hasher.Hash(newPassword!));
        await users.UpdateAsync(user, ct);
        await resets.RevokeAllAsync(user.Id, ct);
        await sessions.RevokeAllAsync(user.Id, ct);
        logger.LogInformation("User {UserId} set a new password with a reset link", user.Id);
        return user;
    }

    /// <summary>Finds or creates the local record for a user authenticated by OIDC or a proxy.</summary>
    public async Task<User> ProvisionExternalAsync(
        UserProvider provider, string subject, string? email, string? displayName, CancellationToken ct)
    {
        var admins = auth.Value.AdminEmails.Select(a => a.Trim().ToLowerInvariant()).ToHashSet();
        var configuredAdmin = admins.Contains(subject.Trim().ToLowerInvariant()) ||
                              (!string.IsNullOrWhiteSpace(email) && admins.Contains(email.Trim().ToLowerInvariant()));

        var trimmed = subject.Trim();
        var user = await users.FindExternalAsync(provider, trimmed, ct);
        if (user is null)
        {
            var (found, created) = await FindOrCreateExternalAsync(provider, trimmed, email, displayName, configuredAdmin, ct);
            if (created) return found;
            user = found; // saved by another request meanwhile: goes on as for any user that is there
        }

        if (user.IsDisabled)
        {
            // A proxy authenticates every request, so its refusals are only a Debug line: a disabled user would else fill the log.
            logger.Log(provider == UserProvider.Proxy ? LogLevel.Debug : LogLevel.Warning, "{Provider} sign-in refused: user {UserId} is disabled", provider, user.Id);
            throw new ForbiddenException(AccountDisabledKey, "This account has been disabled.");
        }
        // Written only when something changed: a proxy authenticates every request, and the row written back as it was read would undo
        // what changed meanwhile, such as the setup page making this user the first administrator.
        var before = (user.Email, user.DisplayName, user.IsAdmin);
        user.UpdateProfile(email, displayName);
        if (configuredAdmin) // configuration only promotes; demotion happens in the UI
        {
            if (!user.IsAdmin) logger.LogInformation("User {UserId} was made an administrator by configuration", user.Id);
            user.SetAdmin(true);
        }
        if ((user.Email, user.DisplayName, user.IsAdmin) != before) await users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>
    /// The user of a provider and subject that was not there a moment ago, and whether this call created it. A proxy authenticates every
    /// request and a page's first ones come together: all of one new user's find nobody and all would insert. One at a time here, so the
    /// others find the row the first saved and no insert fails (EF Core logs a failed save as an Error, handled or not). Another server on
    /// the same database is not held back by this; its insert is refused by the unique index and AddAsync says so: its row is the user,
    /// ours was never saved, so its id would name nobody.
    /// </summary>
    private async Task<(User User, bool Created)> FindOrCreateExternalAsync(
        UserProvider provider, string subject, string? email, string? displayName, bool configuredAdmin, CancellationToken ct)
    {
        var gate = Creating.For($"{provider}\n{subject}");
        await gate.WaitAsync(ct);
        try
        {
            var existing = await users.FindExternalAsync(provider, subject, ct);
            if (existing is not null) return (existing, false);

            var created = User.CreateExternal(provider, subject, email, displayName, configuredAdmin);
            if (await users.AddAsync(created, ct))
            {
                logger.LogInformation("Created user {UserId} from a {Provider} sign-in (administrator: {IsAdmin})", created.Id, provider, created.IsAdmin);
                return (created, true);
            }

            var winner = await users.FindExternalAsync(provider, created.Subject, ct)
                ?? throw new InvalidOperationException("The user another server created is gone again.");
            logger.LogDebug("User {UserId} was created by another server at the same moment", winner.Id);
            return (winner, false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The first administrator of a Standalone instance, not saved yet (the setup page's last step saves it, the only way one is made, #149): the password within the requirements as they are now (<see cref="PasswordRequirements"/>, which
    /// the setup's wizard may have saved a step before; else a <see cref="DomainException"/>), hashed.
    /// </summary>
    public User NewAdministrator(string? email, string? password)
    {
        passwords.Validate(password ?? "");
        var admin = User.CreateLocal(email ?? "", "Administrator", isAdmin: true);
        admin.SetPasswordHash(hasher.Hash(password!));
        return admin;
    }
}
