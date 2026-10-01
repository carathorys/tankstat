using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Users;

namespace Tankstat.Api.Auth;

/// <summary>
/// Trusts the user header set by a reverse proxy, but only when the request really comes from one of the
/// configured proxy addresses; otherwise anyone could send the header themselves.
/// </summary>
internal sealed class ProxyHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IOptions<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var config = auth.Value.ProxyHeader;
        var user = Request.Headers[config.UserHeader];
        if (user.Count == 0) return AuthenticateResult.NoResult();

        if (!IsTrustedProxy(Context.Connection.RemoteIpAddress, config.TrustedProxies))
        {
            Logger.LogWarning("Ignoring {Header} header from untrusted address {Address}.", config.UserHeader, Context.Connection.RemoteIpAddress);
            return AuthenticateResult.NoResult();
        }

        // A single, non-empty value only: multiple values mean the header was appended to by something else.
        var subject = user.Count == 1 ? user[0]?.Trim() : null;
        if (string.IsNullOrEmpty(subject) || subject.Contains(',')) return AuthenticateResult.Fail("Invalid user header.");

        var email = string.IsNullOrEmpty(config.EmailHeader) ? null : Request.Headers[config.EmailHeader].FirstOrDefault();
        try
        {
            var provisioned = await Context.RequestServices.GetRequiredService<AuthService>()
                .ProvisionExternalAsync(UserProvider.Proxy, subject, email, subject, Context.RequestAborted);
            var principal = SessionClaims.Create(provisioned, Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (ForbiddenException e)
        {
            return AuthenticateResult.Fail(e.Message);
        }
    }

    internal static bool IsTrustedProxy(IPAddress? remote, IEnumerable<string> trusted)
    {
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();

        foreach (var entry in trusted)
        {
            var cidr = entry.Contains('/') ? entry : entry.Contains(':') ? $"{entry}/128" : $"{entry}/32";
            if (IPNetwork.TryParse(cidr, out var network) && network.Contains(remote)) return true;
        }
        return false;
    }
}
