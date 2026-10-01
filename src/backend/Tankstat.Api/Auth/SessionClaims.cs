using System.Security.Claims;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

internal static class SessionClaims
{
    public const string CookieScheme = "Cookies";
    public const string OidcScheme = "oidc";
    public const string ProxyScheme = "ProxyHeader";
    public const string VersionClaim = "sv";

    /// <summary>The minimal identity stored in the session: who, and the session version. Everything else is read fresh from the database.</summary>
    public static ClaimsPrincipal Create(User user, string authenticationType) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(VersionClaim, user.SessionVersion.ToString())],
            authenticationType));
}
