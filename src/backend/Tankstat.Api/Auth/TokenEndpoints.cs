using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;

namespace Tankstat.Api.Auth;

/// <summary>
/// The refresh cookie's endpoints (Standalone and OIDC; not found otherwise). They are REST, not GraphQL, because the refresh cookie is
/// only sent to <c>/auth/token/...</c>. A request must carry <c>X-Requested-With: fetch</c>: with SameSite cookies and no CORS a page of
/// another site cannot send that header, so it cannot make a browser refresh or sign out.
/// </summary>
internal static class TokenEndpoints
{
    public const string RequestedWith = "fetch";

    public static void MapTokenEndpoints(this WebApplication app)
    {
        // A new access cookie (and a new refresh token) for the device; 204, or 401 when it has to sign in again.
        app.MapPost("/auth/token/refresh", async (HttpContext http, IOptions<AuthOptions> auth, UserSessionService sessions, CancellationToken ct) =>
        {
            if (Refused(http, auth) is { } refused) return refused;
            try
            {
                var refreshed = await sessions.RefreshAsync(SessionCookies.Refresh(http), ct);
                SessionCookies.SetRefresh(http, refreshed.Token, refreshed.Session.ExpiresAt);
                await SessionCookies.SignInAsync(http, refreshed.User, refreshed.Session.Id);
                return Results.NoContent();
            }
            catch (UnauthenticatedException e)
            {
                await SessionCookies.ClearAsync(http);
                return Results.Json(new { key = e.Key, args = e.Args, message = e.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        // Signs this device out: its session ends and both cookies go, even when the access cookie already ran out.
        app.MapPost("/auth/token/logout", async (HttpContext http, IOptions<AuthOptions> auth, UserSessionService sessions, CancellationToken ct) =>
        {
            if (Refused(http, auth) is { } refused) return refused;
            await sessions.RevokeByTokenAsync(SessionCookies.Refresh(http), ct);
            await SessionCookies.ClearAsync(http);
            return Results.NoContent();
        });
    }

    private static IResult? Refused(HttpContext http, IOptions<AuthOptions> auth)
    {
        if (auth.Value.Mode is not (AuthMode.Standalone or AuthMode.Oidc)) return Results.NotFound();
        return http.Request.Headers["X-Requested-With"] == RequestedWith ? null : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
