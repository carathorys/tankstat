namespace Tankstat.Domain.Users;

/// <summary>Where a user's identity comes from (matches the authentication mode that created it).</summary>
public enum UserProvider
{
    Local,
    Oidc,
    Proxy,
}
