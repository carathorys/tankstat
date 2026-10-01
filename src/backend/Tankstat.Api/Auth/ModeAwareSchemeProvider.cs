using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;

namespace Tankstat.Api.Auth;

/// <summary>
/// The authentication middleware instantiates every remote-authentication scheme on every request and validates
/// its options. The OIDC scheme is therefore hidden unless <c>Auth:Mode</c> is Oidc, so other modes need no OIDC settings.
/// </summary>
internal sealed class ModeAwareSchemeProvider(IOptions<AuthenticationOptions> options, IOptions<AuthOptions> auth)
    : AuthenticationSchemeProvider(options)
{
    private bool Hidden(string name) => name == SessionClaims.OidcScheme && auth.Value.Mode != AuthMode.Oidc;

    public override async Task<AuthenticationScheme?> GetSchemeAsync(string name) =>
        Hidden(name) ? null : await base.GetSchemeAsync(name);

    public override async Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync() =>
        (await base.GetAllSchemesAsync()).Where(s => !Hidden(s.Name)).ToList();

    public override async Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() =>
        (await base.GetRequestHandlerSchemesAsync()).Where(s => !Hidden(s.Name)).ToList();
}
