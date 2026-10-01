using System.Security.Claims;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

/// <summary>
/// Resolves the signed-in user of the current request. The cookie/proxy identity only names the user;
/// admin rights, disabled state and password-change invalidation are always checked against the database.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, IUserRepository users) : ICurrentUser
{
    private Task<Principal?>? _cached;

    public Task<Principal?> GetPrincipalAsync(CancellationToken ct) => _cached ??= ResolveAsync(ct);

    private async Task<Principal?> ResolveAsync(CancellationToken ct)
    {
        var claims = accessor.HttpContext?.User;
        if (claims?.Identity?.IsAuthenticated != true) return null;
        if (!Guid.TryParse(claims.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;

        var user = await users.FindByIdAsync(id, ct);
        if (user is null || user.IsDisabled) return null;
        if (user.Provider == UserProvider.Local &&
            claims.FindFirstValue(SessionClaims.VersionClaim) != user.SessionVersion.ToString()) return null;

        return new Principal(user.Id, user.DisplayName, user.Email, user.IsAdmin);
    }
}
