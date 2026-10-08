using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

/// <summary>
/// The two cookies of a signed-in browser (Standalone and OIDC). The access cookie (<c>tankstat.session</c>) is the usual sign-in ticket,
/// valid for <c>Auth:AccessTokenMinutes</c> and read on every request. The refresh cookie (<c>tankstat.refresh</c>) holds the refresh token
/// of the device's <see cref="UserSession"/>; it is only sent to <c>/auth/token/...</c>, where it buys a new access cookie when the old one
/// ran out. Both are HttpOnly: no script ever sees a token.
/// </summary>
internal static class SessionCookies
{
    public const string RefreshCookie = "tankstat.refresh";
    public const string RefreshPath = "/auth/token";

    /// <summary>Starts a session for the device and gives it its refresh cookie; returns the session's id for the access cookie.</summary>
    public static async Task<Guid> IssueRefreshAsync(HttpContext http, User user, CancellationToken ct)
    {
        var issued = await Sessions(http).IssueAsync(user, ClientLabel.Of(http.Request.Headers.UserAgent.ToString()), ct);
        SetRefresh(http, issued.Token, issued.Session.ExpiresAt);
        return issued.Session.Id;
    }

    /// <summary>A new sign-in: a session, its refresh cookie and an access cookie.</summary>
    public static async Task IssueAsync(HttpContext http, User user, CancellationToken ct) =>
        await SignInAsync(http, user, await IssueRefreshAsync(http, user, ct));

    /// <summary>A new access cookie for an existing session (persistent: it survives closing the browser, for its few minutes).</summary>
    public static Task SignInAsync(HttpContext http, User user, Guid sessionId) =>
        http.SignInAsync(SessionClaims.CookieScheme, SessionClaims.Create(user, SessionClaims.CookieScheme, sessionId), new AuthenticationProperties { IsPersistent = true });

    public static void SetRefresh(HttpContext http, string token, DateTimeOffset expires) =>
        http.Response.Cookies.Append(RefreshCookie, token, Options(http, expires));

    public static string? Refresh(HttpContext http) => http.Request.Cookies[RefreshCookie];

    /// <summary>Removes both cookies (a cookie of another path can still be removed from any response).</summary>
    public static async Task ClearAsync(HttpContext http)
    {
        await http.SignOutAsync(SessionClaims.CookieScheme);
        http.Response.Cookies.Delete(RefreshCookie, Options(http, null));
    }

    /// <summary>The session the access cookie belongs to; none for a cookie from before sessions existed.</summary>
    public static Guid? SessionId(ClaimsPrincipal? user) => Guid.TryParse(user?.FindFirstValue(SessionClaims.SessionClaim), out var id) ? id : null;

    // Like the access cookie: HttpOnly, SameSite=Lax (the OIDC callback is a cross-site redirect), Secure when the request is (plain HTTP
    // behind a TLS proxy is allowed), and only sent to the token endpoints.
    private static CookieOptions Options(HttpContext http, DateTimeOffset? expires) =>
        new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps, Path = RefreshPath, Expires = expires, IsEssential = true };

    private static UserSessionService Sessions(HttpContext http) => http.RequestServices.GetRequiredService<UserSessionService>();
}

/// <summary>Encrypts refresh-token secrets with the key ring that protects the cookies (so a stored secret is as safe as a cookie).</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Tankstat.RefreshTokens.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? Unprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // the key ring was replaced: the device signs in again
        }
    }
}

/// <summary>A coarse name for a device ("Firefox on Linux"), so a person recognises their sessions; the raw User-Agent is never stored.</summary>
internal static class ClientLabel
{
    public static string? Of(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        var browser =
            userAgent.Contains("Edg/") ? "Edge" :
            userAgent.Contains("OPR/") ? "Opera" :
            userAgent.Contains("Firefox/") ? "Firefox" :
            userAgent.Contains("Chrome/") || userAgent.Contains("CriOS/") ? "Chrome" :
            userAgent.Contains("Safari/") ? "Safari" : null;
        var system =
            userAgent.Contains("Android") ? "Android" :
            userAgent.Contains("iPhone") || userAgent.Contains("iPad") ? "iOS" :
            userAgent.Contains("Windows") ? "Windows" :
            userAgent.Contains("Mac OS X") ? "macOS" :
            userAgent.Contains("Linux") ? "Linux" : null;
        return (browser, system) switch
        {
            (null, null) => null,
            ({ } b, null) => b,
            (null, { } s) => s,
            ({ } b, { } s) => $"{b} on {s}",
        };
    }
}
