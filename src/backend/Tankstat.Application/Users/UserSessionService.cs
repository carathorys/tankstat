using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Users;

/// <summary>A new session and the refresh token the device keeps (shown once; only its hash is stored).</summary>
public sealed record IssuedSession(UserSession Session, string Token);

/// <summary>A refreshed session, its user as the database has it now, and the new refresh token.</summary>
public sealed record RefreshedSession(User User, UserSession Session, string Token);

/// <summary>
/// The signed-in devices of a user (Standalone and OIDC modes). Signing in issues a session with a refresh token; while the short access
/// cookie lasts nothing is asked of this service, and when it ran out the device trades its refresh token for a new one (rotation) and a
/// new access cookie. The secret it traded in last is still accepted for <c>Auth:RefreshRotationGraceSeconds</c> (the answer may have been
/// lost, two tabs may refresh at once) and answered with the current secret, not a new one, so all tabs end up holding the same secret;
/// later it means someone else has a copy, and the session ends. Password changes,
/// administrator resets and disabling a user end the sessions, like they already end the access cookies (the session version).
/// </summary>
public sealed class UserSessionService(
    IUserSessionRepository sessions, IUserRepository users, ISecretProtector protector, IOptions<AuthOptions> auth, TimeProvider clock,
    ILogger<UserSessionService> logger)
{
    /// <summary>Sessions that ended are kept this long (for whoever looks into a sign-out), then deleted.</summary>
    private static readonly TimeSpan KeepEnded = TimeSpan.FromDays(7);

    private TimeSpan Lifetime => TimeSpan.FromDays(auth.Value.RefreshTokenDays);

    public async Task<IssuedSession> IssueAsync(User user, string? client, CancellationToken ct)
    {
        var secret = NewSecret();
        var session = UserSession.Issue(user.Id, user.SessionVersion, Hash(secret), protector.Protect(secret), client, clock.GetUtcNow(), Lifetime);
        await sessions.AddAsync(session, ct);
        logger.LogDebug("Session {SessionId} of user {UserId} started", session.Id, user.Id);
        return new IssuedSession(session, Token(session, secret));
    }

    /// <summary>Trades a refresh token for a new one; every refusal is the same <see cref="UnauthenticatedException"/>.</summary>
    public async Task<RefreshedSession> RefreshAsync(string? token, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await sessions.DeleteStaleAsync(now - KeepEnded, ct); // lazily, like the other clean-ups: no background job

        if (!TryParse(token, out var id, out var secret)) throw Refused(null, "it is malformed");
        var session = await sessions.FindAsync(id, ct);
        if (session is null) throw Refused(null, "it is unknown");
        if (!session.IsUsable(now)) throw Refused(session, session.RevokedAt is null ? "it expired" : "it was ended");

        var user = await users.FindByIdAsync(session.UserId, ct);
        if (user is null || user.IsDisabled) throw await EndAsync(session, now, user is null ? "the user does not exist" : "the user is disabled", ct);
        if (user.Provider == UserProvider.Local && user.SessionVersion != session.SessionVersion)
            throw await EndAsync(session, now, "it was signed out by a password change or an administrator", ct);

        var hash = Hash(secret);
        if (!Matches(session.SecretHash, hash))
        {
            var grace = TimeSpan.FromSeconds(auth.Value.RefreshRotationGraceSeconds);
            if (session.PreviousSecretHash is null || !Matches(session.PreviousSecretHash, hash)) throw Refused(session, "the secret is wrong");
            if (session.RotatedAt is not { } rotated || now - rotated > grace)
            {
                logger.LogWarning("Session {SessionId} of user {UserId} ended: a refresh token it had already traded in came back (a copy may be in someone else's hands)", session.Id, session.UserId);
                await sessions.RevokeAsync(session.Id, now, ct);
                throw new UnauthenticatedException();
            }
            return Again(user, session);
        }

        var next = NewSecret();
        session.Rotate(Hash(next), protector.Protect(next), now, Lifetime);
        if (!await sessions.RotateAsync(session, hash, ct))
        {
            // Another refresh with the same secret rotated it a moment before (tabs refreshing at once), or it ended meanwhile: the answer
            // is the one for the session as it is now.
            var current = await sessions.FindAsync(id, ct);
            if (current is null || !current.IsUsable(now) || current.PreviousSecretHash is null || !Matches(current.PreviousSecretHash, hash))
                throw Refused(current, "it changed while it was being refreshed");
            return Again(user, current);
        }
        logger.LogDebug("Session {SessionId} of user {UserId} refreshed", session.Id, session.UserId);
        return new RefreshedSession(user, session, Token(session, next));
    }

    /// <summary>Within the grace: the current secret again, not a new one, so tabs that refreshed at once all hold the same secret.</summary>
    private RefreshedSession Again(User user, UserSession session)
    {
        var current = protector.Unprotect(session.ProtectedSecret) ?? throw Refused(session, "its secret cannot be read back (the key ring changed)");
        logger.LogDebug("Session {SessionId} of user {UserId} refreshed again within the grace", session.Id, session.UserId);
        return new RefreshedSession(user, session, Token(session, current));
    }

    /// <summary>Signs a device out: its session ends (only when the token is the device's own).</summary>
    public async Task<Guid?> RevokeByTokenAsync(string? token, CancellationToken ct)
    {
        if (!TryParse(token, out var id, out var secret) || await sessions.FindAsync(id, ct) is not { } session) return null;
        var hash = Hash(secret);
        if (!Matches(session.SecretHash, hash) && (session.PreviousSecretHash is null || !Matches(session.PreviousSecretHash, hash))) return null;
        return await RevokeAsync(session, ct);
    }

    /// <summary>Ends one session of the user (sign-out from the device whose access cookie names it).</summary>
    public async Task<Guid?> RevokeAsync(Guid sessionId, Guid userId, CancellationToken ct) =>
        await sessions.FindAsync(sessionId, ct) is { } session && session.UserId == userId ? await RevokeAsync(session, ct) : null;

    /// <summary>Ends every session of the user but <paramref name="keep"/> (the device that asked).</summary>
    public async Task<int> RevokeOthersAsync(Guid userId, Guid? keep, CancellationToken ct)
    {
        var count = await sessions.RevokeForUserAsync(userId, keep, clock.GetUtcNow(), ct);
        if (count > 0) logger.LogInformation("{Count} sessions of user {UserId} were ended", count, userId);
        return count;
    }

    public Task<int> RevokeAllAsync(Guid userId, CancellationToken ct) => RevokeOthersAsync(userId, null, ct);

    /// <summary>The device that changed the password stays signed in: its session takes the user's new version.</summary>
    public async Task AdoptVersionAsync(Guid sessionId, User user, CancellationToken ct)
    {
        if (await sessions.FindAsync(sessionId, ct) is not { } session || session.UserId != user.Id) return;
        await sessions.AdoptVersionAsync(session.Id, user.SessionVersion, ct);
    }

    private async Task<Guid?> RevokeAsync(UserSession session, CancellationToken ct)
    {
        if (session.RevokedAt is not null || !await sessions.RevokeAsync(session.Id, clock.GetUtcNow(), ct)) return null;
        logger.LogInformation("Session {SessionId} of user {UserId} signed out", session.Id, session.UserId);
        return session.Id;
    }

    private async Task<UnauthenticatedException> EndAsync(UserSession session, DateTimeOffset now, string reason, CancellationToken ct)
    {
        await sessions.RevokeAsync(session.Id, now, ct);
        return Refused(session, reason);
    }

    /// <summary>The device is only told it is signed out; why is said here (a Debug line: an expired session is ordinary), never the token.</summary>
    private UnauthenticatedException Refused(UserSession? session, string reason)
    {
        if (session is null) logger.LogDebug("Refresh refused: {Reason}", reason);
        else logger.LogDebug("Refresh of session {SessionId} of user {UserId} refused: {Reason}", session.Id, session.UserId, reason);
        return new UnauthenticatedException();
    }

    private static string Token(UserSession session, string secret) => $"{session.Id:N}.{secret}";

    private static bool TryParse(string? token, out Guid id, out string secret)
    {
        var parts = (token ?? "").Split('.', 2);
        secret = parts.Length == 2 ? parts[1] : "";
        id = Guid.Empty;
        return parts.Length == 2 && secret.Length > 0 && Guid.TryParseExact(parts[0], "N", out id);
    }

    private static bool Matches(string storedHash, string hash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedHash), Encoding.UTF8.GetBytes(hash));

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
