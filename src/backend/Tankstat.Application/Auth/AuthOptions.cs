namespace Tankstat.Application.Auth;

public enum AuthMode
{
    /// <summary>No authentication: everyone can see and change everything. Unsafe; the UI shows a warning.</summary>
    None,

    /// <summary>The app stores users and passwords and handles login, password changes and resets itself.</summary>
    Standalone,

    /// <summary>An external OpenID Connect provider authenticates users (server-side code flow).</summary>
    Oidc,

    /// <summary>A trusted reverse proxy (Authelia, Authentik, Cloudflare Access...) authenticates and passes the user in a header.</summary>
    ProxyHeader,
}

/// <summary>
/// Bound from the "Auth" section; environment variables override appsettings.json
/// (e.g. <c>Auth__Mode=Standalone</c>, <c>Auth__Oidc__Authority=...</c>).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.None;

    /// <summary>Public base URL of the app (e.g. https://tankstat.example.com); used in e-mailed links.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Oidc/ProxyHeader modes: identities (e-mail or user name) that are made administrators on login.</summary>
    public List<string> AdminEmails { get; set; } = [];

    /// <summary>Standalone/Oidc: how long the access cookie lasts; a device that is still signed in gets a new one silently (refresh).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Standalone/Oidc: how long a device stays signed in without being used; every use starts it again.</summary>
    public int RefreshTokenDays { get; set; } = 90;

    /// <summary>How long the secret a device held before its last refresh is still accepted (an answer that never arrived, two tabs at once).</summary>
    public int RefreshRotationGraceSeconds { get; set; } = 60;

    public StandaloneOptions Standalone { get; set; } = new();
    public OidcOptions Oidc { get; set; } = new();
    public ProxyHeaderOptions ProxyHeader { get; set; } = new();
}

public sealed class StandaloneOptions
{
    /// <summary>Credentials of the first administrator, used only while no local administrator exists.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int ResetTokenMinutes { get; set; } = 60;

    /// <summary>How long after a link was issued a user's own request for another one sends nothing (0 = no limit).</summary>
    public int ResetCooldownMinutes { get; set; } = 5;

    /// <summary>Lets administrators set a user's password directly (otherwise they can only issue reset links). Off by default.</summary>
    public bool AllowAdminSetPassword { get; set; }
}

public sealed class OidcOptions
{
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];
}

public sealed class ProxyHeaderOptions
{
    public string UserHeader { get; set; } = "X-Forwarded-User";
    public string EmailHeader { get; set; } = "X-Forwarded-Email";

    /// <summary>IP addresses or CIDR ranges of the proxies whose headers are trusted. Required.</summary>
    public List<string> TrustedProxies { get; set; } = [];
}

/// <summary>Bound from the "Smtp" section. Without a host, no e-mail is sent and administrators issue reset links by hand.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }

    /// <summary>Auto, None, StartTls or Ssl.</summary>
    public string Security { get; set; } = "Auto";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}
