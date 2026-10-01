namespace Tankstat.Domain.Users;

/// <summary>A single-use, expiring password reset/setup token. Only a hash of the secret is stored.</summary>
public sealed class PasswordResetToken
{
    private PasswordResetToken() { } // EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string SecretHash { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public static PasswordResetToken Issue(Guid userId, string secretHash, DateTimeOffset now, TimeSpan lifetime) =>
        new() { Id = Guid.NewGuid(), UserId = userId, SecretHash = secretHash, ExpiresAt = now + lifetime };

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
