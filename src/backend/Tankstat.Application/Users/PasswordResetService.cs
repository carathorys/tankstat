using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>
/// What was issued: the one-time token (shown once to the administrator) and how it reached the user, or that the e-mail could not be sent
/// (<see cref="EmailFailed"/>: the link is kept for the administrator to hand over).
/// </summary>
public sealed record IssuedReset(string Token, string? Url, bool EmailSent, bool EmailFailed);

public sealed class PasswordResetService(
    IPasswordResetTokenRepository tokens, IUserRepository users, IEmailSender email,
    IOptions<AuthOptions> auth, TimeProvider clock, ILogger<PasswordResetService> logger)
{
    /// <summary>How long a link is kept after it expired, so following it still says why it was refused.</summary>
    private static readonly TimeSpan KeepExpired = TimeSpan.FromDays(1);

    // Issuing for one user is one step within this process: the look at the latest link (the cool-down) and the replacement of the
    // earlier ones happen under that user's lock, so a burst of requests cannot all pass the cool-down and two issues at once leave one
    // link. The e-mail goes out after it, so a slow mail server holds nobody else up. Other processes on the same database are not held
    // back by it. Striped (by user id) so the locks do not pile up.
    private static readonly StripedLocks Locks = new();

    /// <summary>Whether an issued link can reach the user by e-mail (SMTP and the public address are set up).</summary>
    public bool CanEmail => email.IsConfigured && !string.IsNullOrWhiteSpace(auth.Value.PublicUrl);

    /// <summary>
    /// An administrator's issue: a new link that replaces the user's earlier ones (one live link per user), e-mailed when it can be. When the
    /// e-mail cannot be sent the link is kept and answered with <see cref="IssuedReset.EmailFailed"/>, so the administrator hands it over.
    /// </summary>
    public async Task<IssuedReset> IssueAsync(User user, bool sendEmail, CancellationToken ct) =>
        (await IssueAsync(user, sendEmail, TimeSpan.Zero, keepUnsent: true, ct))!;

    /// <summary>
    /// A user's own request: like <see cref="IssueAsync(User, bool, CancellationToken)"/>, but issues and sends nothing (null) while the
    /// user's latest link was issued less than <c>Auth:Standalone:ResetCooldownMinutes</c> ago and has not expired yet.
    /// </summary>
    public Task<IssuedReset?> IssueUnlessCoolingDownAsync(User user, CancellationToken ct)
    {
        var standalone = auth.Value.Standalone;
        // Never longer than a link lasts: once the latest one expired, the user may ask for another. A link nobody received is taken back
        // whatever the cool-down setting: only an administrator keeps one to hand over.
        return IssueAsync(user, sendEmail: true, TimeSpan.FromMinutes(Math.Min(standalone.ResetCooldownMinutes, standalone.ResetTokenMinutes)), keepUnsent: false, ct);
    }

    private async Task<IssuedReset?> IssueAsync(User user, bool sendEmail, TimeSpan cooldown, bool keepUnsent, CancellationToken ct)
    {
        var options = auth.Value;
        var now = clock.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(options.Standalone.ResetTokenMinutes);
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = PasswordResetToken.Issue(user.Id, Hash(secret), now, lifetime);

        var gate = Locks.For(user.Id);
        await gate.WaitAsync(ct);
        try
        {
            if (cooldown > TimeSpan.Zero && await tokens.LatestIssuedAtAsync(user.Id, ct) is { } latest && latest > now - cooldown) return null;
            // Lazily, like the other clean-ups: no background job.
            await tokens.DeleteStaleAsync(now - lifetime - KeepExpired, ct);
            // Once the earlier links go, the new one is stored whatever becomes of the request.
            await tokens.RemoveForUserAsync(user.Id, CancellationToken.None);
            await tokens.AddAsync(record, CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }

        var token = $"{record.Id:N}.{secret}";
        var url = string.IsNullOrWhiteSpace(options.PublicUrl) ? null : $"{options.PublicUrl.TrimEnd('/')}/?resetToken={token}";

        var sent = false;
        var failed = false;
        if (sendEmail && CanEmail && user.Email.Length > 0)
        {
            try
            {
                await email.SendAsync(user.Email, "Set your Tankstat password",
                    $"Hello {user.DisplayName},\n\nUse this link to set your password (valid for {options.Standalone.ResetTokenMinutes} minutes, one use):\n{url}\n\nIf you did not expect this, ignore this message.", ct);
                sent = true;
            }
            catch (EmailSendException e) when (keepUnsent)
            {
                // The administrator gets the link to hand over instead of an error; why the mail server refused is in the log (the message
                // names the server and the status, never the address). Any other exception is the server's own and escapes below.
                logger.LogWarning(e, "The link for user {UserId} was issued, but the e-mail could not be sent", user.Id);
                failed = true;
            }
            catch
            {
                // Nobody received it, so it is taken back: as the latest link it would make the cool-down hold back the next request.
                await tokens.RemoveForUserAsync(user.Id, CancellationToken.None);
                throw;
            }
        }

        return new IssuedReset(token, url, sent, failed);
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
