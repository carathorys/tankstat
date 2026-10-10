using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

public static class AuthExtensions
{
    /// <summary>
    /// Registers every authentication scheme but selects the active one from <c>Auth:Mode</c> lazily, so the
    /// mode (and the OIDC settings) can come from appsettings.json, environment variables or the command line.
    /// </summary>
    public static IServiceCollection AddAuthModes(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddPersistedKeyRing();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddAuthentication()
            .AddCookie(SessionClaims.CookieScheme, o =>
            {
                o.Cookie.Name = "tankstat.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax; // Lax (not Strict) so the redirect back from the OIDC provider keeps the session
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // plain HTTP is allowed behind a TLS-terminating proxy
                // Short-lived (Auth:AccessTokenMinutes, set below) and not sliding: the refresh cookie keeps a device signed in (SessionCookies).
                o.SlidingExpiration = false;
                // This is an API: answer with a status code instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            })
            .AddOpenIdConnect(SessionClaims.OidcScheme, o =>
            {
                o.SignInScheme = SessionClaims.CookieScheme;
                o.ResponseType = "code";
                o.UsePkce = true;
                o.SaveTokens = false;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = true;
                o.CallbackPath = "/auth/oidc/callback";
                o.SignedOutCallbackPath = "/auth/oidc/signed-out";
                o.Events.OnTokenValidated = ProvisionOidcUser;
                // A sign-in that ends without a session (the user cancelled, the provider refused, a stale callback, a disabled account) sends
                // the browser back to the app with a reason code; the sign-in screen explains it instead of trying again at once.
                o.Events.OnRemoteFailure = ctx => FailSignIn(ctx, OidcFailures.Classify(ctx.Failure), ctx.Properties?.RedirectUri ?? (ctx.Failure as OidcSignInRefusedException)?.ReturnUrl, ctx.Failure?.GetType().Name);
                o.Events.OnAccessDenied = ctx => FailSignIn(ctx, OidcFailure.AccessDenied, ctx.ReturnUrl, null);
            })
            .AddScheme<AuthenticationSchemeOptions, ProxyHeaderAuthenticationHandler>(SessionClaims.ProxyScheme, null);

        services.AddSingleton<IAuthenticationSchemeProvider, ModeAwareSchemeProvider>();

        // The default scheme follows the mode, like the schemes ModeAwareSchemeProvider shows: the proxy's headers in ProxyHeader mode, the
        // cookie in Standalone and OIDC, and none in mode None, so a sign-in cookie left over from another mode never names a user there
        // (every visitor is the anonymous owner, as the mode promises). Nothing may then authenticate or challenge without naming a scheme.
        services.AddOptions<AuthenticationOptions>().Configure<IOptions<AuthOptions>>((o, auth) =>
        {
            o.DefaultScheme = auth.Value.Mode switch
            {
                AuthMode.ProxyHeader => SessionClaims.ProxyScheme,
                AuthMode.None => null,
                _ => SessionClaims.CookieScheme,
            };
            if (auth.Value.Mode == AuthMode.Oidc) o.DefaultChallengeScheme = SessionClaims.OidcScheme;
        });

        services.AddOptions<CookieAuthenticationOptions>(SessionClaims.CookieScheme).Configure<IOptions<AuthOptions>>((o, auth) =>
            o.ExpireTimeSpan = TimeSpan.FromMinutes(auth.Value.AccessTokenMinutes));

        services.AddOptions<OpenIdConnectOptions>(SessionClaims.OidcScheme).Configure<IOptions<AuthOptions>>((o, auth) =>
        {
            var oidc = auth.Value.Oidc;
            o.Authority = oidc.Authority;
            o.ClientId = oidc.ClientId;
            o.ClientSecret = oidc.ClientSecret;
            o.RequireHttpsMetadata = oidc.Authority?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? true;
            o.Scope.Clear();
            foreach (var scope in oidc.Scopes) o.Scope.Add(scope);
        });

        return services;
    }

    private static async Task ProvisionOidcUser(TokenValidatedContext ctx)
    {
        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthExtensions));
        var claims = ctx.Principal!;
        var subject = claims.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subject))
        {
            Refuse(ctx, OidcFailure.NoSubject, cause: null); // the failure handler logs it
            return;
        }

        // Only an explicitly unverified address is withheld: it must not be able to claim an administrator e-mail.
        var email = claims.FindFirstValue("email");
        if (claims.FindFirstValue("email_verified") == "false") email = null;
        var name = claims.FindFirstValue("name") ?? claims.FindFirstValue("preferred_username");

        try
        {
            var user = await ctx.HttpContext.RequestServices.GetRequiredService<AuthService>()
                .ProvisionExternalAsync(UserProvider.Oidc, subject, email, name, ctx.HttpContext.RequestAborted);
            // The remote handler signs this principal into the access cookie itself: only the session and its refresh cookie are made here.
            var sessionId = await SessionCookies.IssueRefreshAsync(ctx.HttpContext, user, ctx.HttpContext.RequestAborted);
            ctx.Principal = SessionClaims.Create(user, SessionClaims.OidcScheme, sessionId);
            ctx.Properties!.IsPersistent = true; // like a password sign-in: closing the browser does not sign the device out
            logger.LogInformation("User {UserId} signed in through OIDC", user.Id); // never the subject: some providers use the e-mail address for it
        }
        catch (ForbiddenException e)
        {
            Refuse(ctx, OidcFailures.Classify(e), cause: e); // AuthService logged the refusal with the user id; the failure handler adds the outcome
        }
    }

    /// <summary>
    /// Ends a failed sign-in: one Warning with the reason code (never the provider's text, which is under its control) and a redirect to the
    /// page the sign-in meant to return to, marked as failed.
    /// </summary>
    /// <summary>Ends the sign-in with a reason. The page the user wanted travels in the exception: the framework drops the properties of a failed token event.</summary>
    private static void Refuse(TokenValidatedContext ctx, OidcFailure reason, Exception? cause) =>
        ctx.Fail(new OidcSignInRefusedException(reason, ctx.Properties?.RedirectUri, cause));

    private static Task FailSignIn(HandleRequestContext<RemoteAuthenticationOptions> ctx, OidcFailure reason, string? returnUrl, string? failureType)
    {
        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthExtensions));
        logger.LogWarning("OIDC sign-in failed: {Reason} ({FailureType})", OidcFailures.Code(reason), failureType ?? "none");
        ctx.Response.Redirect(OidcFailures.FailedUrl(reason, returnUrl));
        ctx.HandleResponse();
        return Task.CompletedTask;
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Starts the OIDC code flow; the provider redirects back to /auth/oidc/callback, handled by the OIDC middleware.
        app.MapGet("/auth/oidc/login", (HttpContext http, IOptions<AuthOptions> auth, string? returnUrl) =>
            auth.Value.Mode != AuthMode.Oidc
                ? Results.NotFound()
                : Results.Challenge(new AuthenticationProperties { RedirectUri = LocalPathOrRoot(returnUrl) }, [SessionClaims.OidcScheme]));
        app.MapTokenEndpoints();
    }

    /// <summary>
    /// Only same-site paths are allowed as redirect target (no open redirect): one leading slash, and printable ASCII only. A browser drops a
    /// TAB before parsing, so "/\t/evil" would read as "//evil"; CR, LF and non-ASCII would make the redirect header itself invalid.
    /// </summary>
    internal static string LocalPathOrRoot(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
        && returnUrl.All(c => c is >= ' ' and <= '~')
            ? returnUrl : "/";
}
