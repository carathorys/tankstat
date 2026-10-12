namespace Tankstat.Application.SystemSettings;

/// <summary>What an OpenID Connect authority answered at <c>/.well-known/openid-configuration</c>: its issuer, or why there was no document.</summary>
/// <param name="Reason">A code when the document could not be had: <c>not_a_url</c>, <c>unreachable</c>, <c>timeout</c>, <c>redirected</c>, <c>status_404</c>, <c>too_large</c>, <c>not_json</c>, <c>no_issuer</c>.</param>
public sealed record OidcDiscoveryResult(bool Reachable, string? Issuer, string? Reason)
{
    public static OidcDiscoveryResult Found(string issuer) => new(true, issuer, null);
    public static OidcDiscoveryResult Failed(string reason) => new(false, null, reason);
}

/// <summary>Reads the discovery document of an authority an administrator typed, before it is saved: a mistyped one would lock everybody out.</summary>
public interface IOidcDiscovery
{
    Task<OidcDiscoveryResult> ProbeAsync(string authority, CancellationToken ct);
}
