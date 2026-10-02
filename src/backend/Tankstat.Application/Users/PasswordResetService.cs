using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>What was issued: the one-time token (shown once to the administrator) and how it reached the user.</summary>
public sealed record IssuedReset(string Token, string? Url, bool EmailSent);

public sealed class PasswordResetService(
    IPasswordResetTokenRepository tokens, IUserRepository users, IEmailSender email,
    IOptions<AuthOptions> auth, TimeProvider clock)
{
    public async Task<IssuedReset> IssueAsync(User user, bool sendEmail, CancellationToken ct)
    {
        var options = auth.Value;
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = PasswordResetToken.Issue(user.Id, Hash(secret), clock.GetUtcNow(), TimeSpan.FromMinutes(options.Standalone.ResetTokenMinutes));
        await tokens.AddAsync(record, ct);

        var token = $"{record.Id:N}.{secret}";
        var url = string.IsNullOrWhiteSpace(options.PublicUrl) ? null : $"{options.PublicUrl.TrimEnd('/')}/?resetToken={token}";

        var sent = false;
        if (sendEmail && email.IsConfigured && url is not null && user.Email.Length > 0)
        {
            await email.SendAsync(user.Email, "Set your Tankstat password",
                $"Hello {user.DisplayName},\n\nUse this link to set your password (valid for {options.Standalone.ResetTokenMinutes} minutes, one use):\n{url}\n\nIf you did not expect this, ignore this message.", ct);
            sent = true;
        }

        return new IssuedReset(token, url, sent);
    }

    /// <summary>Validates the token and returns it with its user; throws the same vague error for every failure.</summary>
    public async Task<(User User, PasswordResetToken Token)> ResolveAsync(string? token, CancellationToken ct)
    {
        var invalid = new DomainException("reset.invalid", "This password reset link is invalid or has expired.");
        var parts = (token ?? "").Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id)) throw invalid;

        var record = await tokens.FindAsync(id, ct);
        if (record is null || !record.IsUsable(clock.GetUtcNow())) throw invalid;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(record.SecretHash), Encoding.UTF8.GetBytes(Hash(parts[1])))) throw invalid;

        var user = await users.FindByIdAsync(record.UserId, ct);
        if (user is null || user.IsDisabled || user.Provider != UserProvider.Local) throw invalid;
        return (user, record);
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
