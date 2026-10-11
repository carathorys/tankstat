using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>What became of the e-mail with an issued link.</summary>
public enum EmailOutcome
{
    /// <summary>None was tried: no SMTP or public address, no e-mail address, or none was asked for.</summary>
    NotTried,

    /// <summary>The mail server took it.</summary>
    Sent,

    /// <summary>It could not be sent (<see cref="EmailSendException"/>); the link is kept for the administrator to hand over.</summary>
    Failed,
}

/// <summary>What was issued: the one-time token (shown once to the administrator) and what became of the e-mail that carries it.</summary>
public sealed record IssuedReset(string Token, string? Url, EmailOutcome Email)
{
    public bool EmailSent => Email == EmailOutcome.Sent;
    public bool EmailFailed => Email == EmailOutcome.Failed;
}

public sealed class PasswordResetService(
    IPasswordResetTokenRepository tokens, IUserRepository users, IEmailSender email,
    IOptions<AuthOptions> auth, TimeProvider clock, ILogger<PasswordResetService> logger)
{
    /// <summary>How long a link is kept after it expired, so following it still says why it was refused.</summary>
    private static readonly TimeSpan KeepExpired = TimeSpan.FromDays(1);

    // Issuing for one user is one step within this process: the look at the latest link (the cool-down), the storing of the new one and
    // the note of which links were there before it happen under that user's lock, so the mailer serving a user's own request never stores
    // its link after an administrator's that its look at the cool-down did not see (an administrator's issue has no cool-down), and each
    // issue knows exactly the links it replaces (a burst of the user's own requests never gets this far: the queue holds a user once). The
    // e-mail goes out after it, so a slow mail server holds nobody else up. Other processes on the same database are not held back by it.
    // Striped (by user id) so the locks do not pile up.
    private static readonly StripedLocks Locks = new();

    /// <summary>Whether an issued link can reach the user by e-mail (SMTP and the public address are set up).</summary>
    public bool CanEmail => email.IsConfigured && !string.IsNullOrWhiteSpace(auth.Value.PublicUrl);

    /// <summary>
    /// An administrator's issue: a new link that replaces the user's earlier ones (one live link per user), e-mailed when it can be. When the
    /// e-mail cannot be sent the link is kept and answered with <see cref="EmailOutcome.Failed"/>, so the administrator hands it over; any
    /// other failure takes the new link back, leaves the earlier ones and escapes.
    /// </summary>
    public async Task<IssuedReset> IssueAsync(User user, bool sendEmail, CancellationToken ct) =>
        (await IssueAsync(user, sendEmail, TimeSpan.Zero, keepUnsent: true, ct))!;

    /// <summary>
    /// A user's own request: like <see cref="IssueAsync(User, bool, CancellationToken)"/>, but issues and sends nothing (null) while the
    /// user's latest link was issued less than <c>Auth:Standalone:ResetCooldownMinutes</c> ago and has not expired yet; and a link whose
    /// e-mail could not be sent is taken back, the earlier ones stay and the <see cref="EmailSendException"/> escapes (never
    /// <see cref="EmailOutcome.Failed"/>).
    /// </summary>
    private Task<IssuedReset?> IssueUnlessCoolingDownAsync(User user, CancellationToken ct)
    {
        var standalone = auth.Value.Standalone;
        // Never longer than a link lasts: once the latest one expired, the user may ask for another. A link nobody received is taken back
        // whatever the cool-down setting: only an administrator keeps one to hand over.
        return IssueAsync(user, sendEmail: true, TimeSpan.FromMinutes(Math.Min(standalone.ResetCooldownMinutes, standalone.ResetTokenMinutes)), keepUnsent: false, ct);
    }

    /// <summary>
    /// Serves a request the user made through <i>Forgot your password?</i>, after it was answered (<see cref="IPasswordResetQueue"/>). Looks
    /// at the account again, since it may have been disabled or the mail settings changed meanwhile, then e-mails a link unless the cool-down
    /// holds it back (<see cref="IssueUnlessCoolingDownAsync"/>). Sends nothing when the user's <see cref="User.SessionVersion"/> is no
    /// longer <paramref name="sessionVersion"/>, the one the request was made with: a new password since then revoked every link (and so
    /// did disabling the account), and a link issued now would outlive that. A mail server's failure is a Warning: the link is taken back,
    /// so the user can simply ask again. Anything else escapes to the worker, a cancellation included (the link is taken back then too).
    /// </summary>
    public async Task SendRequestedAsync(Guid userId, int sessionVersion, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || user.IsDisabled || user.Provider != UserProvider.Local)
        {
            logger.LogInformation("The password reset requested by user {UserId} was not sent: the account no longer signs in with a password", userId);
            return;
        }
        if (user.SessionVersion != sessionVersion)
        {
            logger.LogInformation("The password reset requested by user {UserId} was not sent: the password was set or the account disabled after the request", userId);
            return;
        }
        if (!CanEmail)
        {
            logger.LogInformation("The password reset requested by user {UserId} was not sent: no e-mail can be sent", userId);
            return;
        }

        IssuedReset? issued;
        try
        {
            issued = await IssueUnlessCoolingDownAsync(user, ct);
        }
        catch (EmailSendException e)
        {
            // Its text names the server and the status, never the address.
            logger.LogWarning(e, "The password reset e-mail requested by user {UserId} could not be sent; its link was taken back", userId);
            return;
        }
        if (issued is null)
        {
            logger.LogInformation("User {UserId} requested a password reset within the cool-down of their latest link; nothing was sent", userId);
            return;
        }
        logger.LogInformation("The password reset requested by user {UserId} was issued (e-mail: {Email})", userId, issued.Email);
    }

    private async Task<IssuedReset?> IssueAsync(User user, bool sendEmail, TimeSpan cooldown, bool keepUnsent, CancellationToken ct)
    {
        var options = auth.Value;
        var lifetime = TimeSpan.FromMinutes(options.Standalone.ResetTokenMinutes);
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));

        PasswordResetToken record;
        IReadOnlyList<Guid> earlier;
        var gate = Locks.For(user.Id);
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (cooldown > TimeSpan.Zero && await tokens.LatestIssuedAtAsync(user.Id, ct) is { } latest && latest > now - cooldown) return null;
            // Lazily, like the other clean-ups: no background job.
            await tokens.DeleteStaleAsync(now - lifetime - KeepExpired, ct);
            // The links there before this one, which it replaces once it reached the user (or was kept for the administrator); a link
            // issued after it is not among them, so two issues at once never remove each other's.
            earlier = await tokens.IdsForUserAsync(user.Id, ct);
            record = PasswordResetToken.Issue(user.Id, Hash(secret), now, lifetime);
            // Stored whatever becomes of the request from here on.
            await tokens.AddAsync(record, CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }

        var token = $"{record.Id:N}.{secret}";
        var url = string.IsNullOrWhiteSpace(options.PublicUrl) ? null : $"{options.PublicUrl.TrimEnd('/')}/?resetToken={token}";

        var outcome = EmailOutcome.NotTried;
        if (sendEmail && CanEmail && user.Email.Length > 0)
        {
            try
            {
                await email.SendAsync(user.Email, "Set your Tankstat password",
                    $"Hello {user.DisplayName},\n\nUse this link to set your password (valid for {options.Standalone.ResetTokenMinutes} minutes, one use):\n{url}\n\nIf you did not expect this, ignore this message.", ct);
                outcome = EmailOutcome.Sent;
            }
            catch (EmailSendException e) when (keepUnsent)
            {
                // The administrator gets the link to hand over instead of an error; why the mail server refused is in the log (the message
                // names the server and the status, never the address). Any other exception is the server's own and escapes below.
                logger.LogWarning(e, "The link for user {UserId} was issued, but the e-mail could not be sent", user.Id);
                outcome = EmailOutcome.Failed;
            }
            catch
            {
                // Nobody received it, so it is taken back (as the latest link it would make the cool-down hold back the next request), and
                // only it: the earlier links stay, a link handed over by an administrator among them, and so does one issued meanwhile.
                await tokens.RemoveAsync([record.Id], CancellationToken.None);
                throw;
            }
        }

        await tokens.RemoveAsync(earlier, CancellationToken.None);
        return new IssuedReset(token, url, outcome);
    }

    /// <summary>Validates the token and returns it with its user; throws the same vague error for every failure.</summary>
    public async Task<(User User, PasswordResetToken Token)> ResolveAsync(string? token, CancellationToken ct)
    {
        var parts = (token ?? "").Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id)) throw Refuse("malformed link", null);

        var record = await tokens.FindAsync(id, ct);
        if (record is null) throw Refuse("unknown link", null);
        if (!record.IsUsable(clock.GetUtcNow())) throw Refuse("expired or already used", record.UserId);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(record.SecretHash), Encoding.UTF8.GetBytes(Hash(parts[1])))) throw Refuse("wrong secret", record.UserId);

        var user = await users.FindByIdAsync(record.UserId, ct);
        if (user is null || user.IsDisabled || user.Provider != UserProvider.Local) throw Refuse("the account cannot sign in", record.UserId);
        return (user, record);
    }

    /// <summary>The same vague error for every cause, so a link reveals nothing; only the log line says which one (never any part of the token).</summary>
    private DomainException Refuse(string reason, Guid? userId)
    {
        logger.LogInformation("Password reset link refused: {Reason} (user {UserId})", reason, userId?.ToString() ?? "unknown");
        return new DomainException("reset.invalid", "This password reset link is invalid or has expired.");
    }

    public Task MarkUsedAsync(PasswordResetToken token, CancellationToken ct)
    {
        token.MarkUsed(clock.GetUtcNow());
        return tokens.UpdateAsync(token, ct);
    }

    /// <summary>Invalidates every link the user was ever sent; call after their password changed.</summary>
    public Task RevokeAllAsync(Guid userId, CancellationToken ct) => tokens.RemoveForUserAsync(userId, ct);

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
