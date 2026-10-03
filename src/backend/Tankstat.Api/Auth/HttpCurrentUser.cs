using System.Security.Claims;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

/// <summary>
/// Resolves the signed-in user of the current request. The cookie/proxy identity only names the user;
/// admin rights, disabled state and password-change invalidation are always checked against the database.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, IUserRepository users, ILogger<HttpCurrentUser> logger) : ICurrentUser
{
    private Task<Principal?>? _cached;

    public Task<Principal?> GetPrincipalAsync(CancellationToken ct) => _cached ??= ResolveAsync(ct);

    private async Task<Principal?> ResolveAsync(CancellationToken ct)
    {
        var claims = accessor.HttpContext?.User;
        if (claims?.Identity?.IsAuthenticated != true) return null;
        // A session that is not (or no longer) valid only looks like "not signed in" to the client; the notification bell asks every minute,
        // so these are Debug lines, there for whoever asks why somebody was signed out.
        if (!Guid.TryParse(claims.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            logger.LogDebug("Session rejected: its user id is missing or invalid");
            return null;
        }

        var user = await users.FindByIdAsync(id, ct);
        if (user is null || user.IsDisabled)
        {
            logger.LogDebug("Session of user {UserId} rejected: {Reason}", id, user is null ? "the user does not exist" : "the user is disabled");
            return null;
        }
        if (user.Provider == UserProvider.Local &&
            claims.FindFirstValue(SessionClaims.VersionClaim) != user.SessionVersion.ToString())
        {
            logger.LogDebug("Session of user {UserId} rejected: it was signed out by a password change or an administrator", id);
            return null;
        }

        return new Principal(user.Id, user.DisplayName, user.Email, user.IsAdmin, user.AvatarImageId);
    }
}
