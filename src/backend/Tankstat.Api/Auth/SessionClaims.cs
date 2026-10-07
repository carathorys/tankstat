using System.Security.Claims;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

internal static class SessionClaims
{
    public const string CookieScheme = "Cookies";
    public const string OidcScheme = "oidc";
    public const string ProxyScheme = "ProxyHeader";
    public const string VersionClaim = "sv";
    public const string SessionClaim = "sid";

    /// <summary>
    /// The minimal identity stored in the access cookie: who, the session version and the device's session (none behind a proxy, which
    /// signs every request in itself). Everything else is read fresh from the database.
    /// </summary>
    public static ClaimsPrincipal Create(User user, string authenticationType, Guid? sessionId = null)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(VersionClaim, user.SessionVersion.ToString())];
        if (sessionId is { } id) claims.Add(new Claim(SessionClaim, id.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}
