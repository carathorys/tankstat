using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>Sign-in related use cases for every mode: local login/password handling and external user provisioning.</summary>
public sealed class AuthService(
    IUserRepository users, IPasswordHasher hasher, PasswordResetService resets, AccessService access,
    IOptions<AuthOptions> auth, TimeProvider clock, ILogger<AuthService> logger)
{
    /// <summary>The error key of a refused sign-in of a disabled user; the OIDC failure handler recognises it.</summary>
    public const string AccountDisabledKey = "auth.accountDisabled";

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

        user.RegisterSuccessfulLogin();
        await users.UpdateAsync(user, ct);
        logger.LogInformation("User {UserId} signed in", user.Id);
        return user;
    }

    /// <summary>Changes the signed-in user's password; the returned user carries the new session version.</summary>
    public async Task<User> ChangePasswordAsync(string? currentPassword, string? newPassword, CancellationToken ct)
    {
        RequireMode(AuthMode.Standalone);
        var principal = await access.RequirePrincipalAsync(ct);
        var user = await users.FindByIdAsync(principal.Id, ct) ?? throw new UnauthenticatedException();

        if (user.PasswordHash is null || !hasher.Verify(user.PasswordHash, currentPassword ?? ""))
        {
            logger.LogWarning("Password change refused for user {UserId}: the current password is incorrect", user.Id);
            throw new DomainException("password.currentIncorrect", "The current password is not correct.");
        }
        PasswordPolicy.Validate(newPassword);

        user.SetPasswordHash(hasher.Hash(newPassword!));
        await users.UpdateAsync(user, ct);
        await resets.RevokeAllAsync(user.Id, ct);
        logger.LogInformation("User {UserId} changed their password", user.Id);
        return user;
    }

    /// <summary>Always succeeds from the caller's view, whether or not the e-mail is registered.</summary>
    public async Task RequestPasswordResetAsync(string? email, CancellationToken ct)
    {
        RequireMode(AuthMode.Standalone);
        var user = await users.FindLocalByEmailAsync(User.NormalizeEmailOrEmpty(email), ct);
        if (user is null || user.IsDisabled)
        {
            logger.LogInformation("A password reset was requested for an unknown or disabled account");
            return;
        }
        var issued = await resets.IssueAsync(user, sendEmail: true, ct);
        logger.LogInformation("User {UserId} requested a password reset (e-mail sent: {EmailSent})", user.Id, issued.EmailSent);
    }

    public async Task<User> ResetPasswordAsync(string? token, string? newPassword, CancellationToken ct)
    {
        RequireMode(AuthMode.Standalone);
        var (user, record) = await resets.ResolveAsync(token, ct);
        PasswordPolicy.Validate(newPassword);

        await resets.MarkUsedAsync(record, ct);
        user.SetPasswordHash(hasher.Hash(newPassword!));
        await users.UpdateAsync(user, ct);
        await resets.RevokeAllAsync(user.Id, ct);
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

        var user = await users.FindExternalAsync(provider, subject.Trim(), ct);
        if (user is null)
        {
            user = User.CreateExternal(provider, subject, email, displayName, configuredAdmin);
            await users.AddAsync(user, ct);
            logger.LogInformation("Created user {UserId} from a {Provider} sign-in (administrator: {IsAdmin})", user.Id, provider, user.IsAdmin);
            return user;
        }

        if (user.IsDisabled)
        {
            // A proxy authenticates every request, so its refusals are only a Debug line: a disabled user would else fill the log.
            logger.Log(provider == UserProvider.Proxy ? LogLevel.Debug : LogLevel.Warning, "{Provider} sign-in refused: user {UserId} is disabled", provider, user.Id);
            throw new ForbiddenException(AccountDisabledKey, "This account has been disabled.");
        }
        user.UpdateProfile(email, displayName);
        if (configuredAdmin) // configuration only promotes; demotion happens in the UI
        {
            if (!user.IsAdmin) logger.LogInformation("User {UserId} was made an administrator by configuration", user.Id);
            user.SetAdmin(true);
        }
        await users.UpdateAsync(user, ct);
        return user;
    }

    /// <summary>Standalone mode: creates the first administrator from configuration if no local administrator exists yet.</summary>
    public async Task BootstrapAdminAsync(CancellationToken ct)
    {
        if (auth.Value.Mode != AuthMode.Standalone || await users.AnyLocalAdminAsync(ct)) return;

        var options = auth.Value.Standalone;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
            throw new InvalidOperationException(
                "Auth:Mode is Standalone but no administrator exists. Set Auth:Standalone:AdminEmail and Auth:Standalone:AdminPassword (e.g. Auth__Standalone__AdminEmail) for the first start.");

        PasswordPolicy.Validate(options.AdminPassword);
        var admin = User.CreateLocal(options.AdminEmail, "Administrator", isAdmin: true);
        admin.SetPasswordHash(hasher.Hash(options.AdminPassword));
        await users.AddAsync(admin, ct);
        logger.LogInformation("Created the first administrator {UserId}", admin.Id);
    }
}
