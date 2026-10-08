namespace Tankstat.Domain.Users;

/// <summary>
/// A signed-in device: what keeps it signed in for longer than its short-lived access cookie. It is identified by a refresh token whose
/// secret only the device has (only a hash is stored); every refresh replaces the secret (<see cref="Rotate"/>) and moves the expiry on,
/// so a device that is used stays signed in, and one that is not used for <c>Auth:RefreshTokenDays</c> has to sign in again.
/// </summary>
public sealed class UserSession
{
    public const int MaxClientLength = 120;

    private UserSession() { } // EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>The hash of the secret the device holds now (what a presented token is compared with).</summary>
    public string SecretHash { get; private set; } = "";

    /// <summary>
    /// The same secret, encrypted with the key ring that protects the cookies: a device that comes back within the grace with the secret
    /// it had before (an answer that never arrived, two tabs refreshing at once) gets this one again, so every tab ends up with one secret.
    /// </summary>
    public string ProtectedSecret { get; private set; } = "";

    /// <summary>The hash of the secret it held before the last refresh: accepted for a short while (an answer that never arrived), then a sign of theft.</summary>
    public string? PreviousSecretHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The user's <c>SessionVersion</c> when it was issued: a password change or an administrator ends it.</summary>
    public int SessionVersion { get; private set; }

    /// <summary>A coarse description of the device ("Firefox on Linux"), worked out on the server; never the raw header.</summary>
    public string? Client { get; private set; }

    public static UserSession Issue(
        Guid userId, int sessionVersion, string secretHash, string protectedSecret, string? client, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, SessionVersion = sessionVersion, SecretHash = secretHash, ProtectedSecret = protectedSecret,
        Client = client is { Length: > MaxClientLength } ? client[..MaxClientLength] : client,
        CreatedAt = now, LastUsedAt = now, ExpiresAt = now + lifetime,
    };

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Hands the device a new secret and gives it a full lifetime again from now.</summary>
    public void Rotate(string newSecretHash, string newProtectedSecret, DateTimeOffset now, TimeSpan lifetime)
    {
        PreviousSecretHash = SecretHash;
        SecretHash = newSecretHash;
        ProtectedSecret = newProtectedSecret;
        RotatedAt = now;
        LastUsedAt = now;
        ExpiresAt = now + lifetime;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>The user's version moved on while this device stays signed in (it changed the password itself).</summary>
    public void AdoptVersion(int sessionVersion) => SessionVersion = sessionVersion;
}
