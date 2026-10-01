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

        services.AddAuthentication()
            .AddCookie(SessionClaims.CookieScheme, o =>
            {
                o.Cookie.Name = "tankstat.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax; // Lax (not Strict) so the redirect back from the OIDC provider keeps the session
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // plain HTTP is allowed behind a TLS-terminating proxy
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
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
            })
            .AddScheme<AuthenticationSchemeOptions, ProxyHeaderAuthenticationHandler>(SessionClaims.ProxyScheme, null);

        services.AddSingleton<IAuthenticationSchemeProvider, ModeAwareSchemeProvider>();

        services.AddOptions<AuthenticationOptions>().Configure<IOptions<AuthOptions>>((o, auth) =>
        {
            o.DefaultScheme = auth.Value.Mode == AuthMode.ProxyHeader ? SessionClaims.ProxyScheme : SessionClaims.CookieScheme;
            if (auth.Value.Mode == AuthMode.Oidc) o.DefaultChallengeScheme = SessionClaims.OidcScheme;
        });

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
        var claims = ctx.Principal!;
        var subject = claims.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subject)) { ctx.Fail("The provider did not return a subject."); return; }

        // Only an explicitly unverified address is withheld: it must not be able to claim an administrator e-mail.
        var email = claims.FindFirstValue("email");
        if (claims.FindFirstValue("email_verified") == "false") email = null;
        var name = claims.FindFirstValue("name") ?? claims.FindFirstValue("preferred_username");

        try
        {
            var user = await ctx.HttpContext.RequestServices.GetRequiredService<AuthService>()
                .ProvisionExternalAsync(UserProvider.Oidc, subject, email, name, ctx.HttpContext.RequestAborted);
            ctx.Principal = SessionClaims.Create(user, SessionClaims.OidcScheme);
        }
        catch (ForbiddenException e)
        {
            ctx.Fail(e.Message);
        }
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Starts the OIDC code flow; the provider redirects back to /auth/oidc/callback, handled by the OIDC middleware.
        app.MapGet("/auth/oidc/login", (HttpContext http, IOptions<AuthOptions> auth, string? returnUrl) =>
            auth.Value.Mode != AuthMode.Oidc
                ? Results.NotFound()
                : Results.Challenge(new AuthenticationProperties { RedirectUri = LocalPathOrRoot(returnUrl) }, [SessionClaims.OidcScheme]));
    }

    /// <summary>Only same-site paths are allowed as redirect target (no open redirect).</summary>
    internal static string LocalPathOrRoot(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            ? returnUrl : "/";
}
