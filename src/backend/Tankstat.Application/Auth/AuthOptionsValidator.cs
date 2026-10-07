using Microsoft.Extensions.Options;

namespace Tankstat.Application.Auth;

public sealed class AuthOptionsValidator(IOptions<SmtpOptions> smtp) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions o)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(o.Mode)) errors.Add($"Auth:Mode '{o.Mode}' is not supported.");
        if (o.AccessTokenMinutes is < 1 or > 1440) errors.Add("Auth:AccessTokenMinutes must be between 1 and 1440.");
        if (o.RefreshTokenDays is < 1 or > 3650) errors.Add("Auth:RefreshTokenDays must be between 1 and 3650.");
        if (o.RefreshRotationGraceSeconds is < 0 or > 3600) errors.Add("Auth:RefreshRotationGraceSeconds must be between 0 and 3600.");

        if (o.Mode == AuthMode.Oidc)
        {
            if (string.IsNullOrWhiteSpace(o.Oidc.Authority)) errors.Add("Auth:Oidc:Authority is required for Oidc mode.");
            if (string.IsNullOrWhiteSpace(o.Oidc.ClientId)) errors.Add("Auth:Oidc:ClientId is required for Oidc mode.");
            if (string.IsNullOrWhiteSpace(o.Oidc.ClientSecret)) errors.Add("Auth:Oidc:ClientSecret is required for Oidc mode.");
        }

        if (o.Mode == AuthMode.ProxyHeader)
        {
            if (string.IsNullOrWhiteSpace(o.ProxyHeader.UserHeader)) errors.Add("Auth:ProxyHeader:UserHeader is required.");
            if (o.ProxyHeader.TrustedProxies.Count == 0)
                errors.Add("Auth:ProxyHeader:TrustedProxies must list the proxy IP addresses/CIDR ranges, otherwise anyone could forge the header.");
        }

        if (o.Mode == AuthMode.Standalone && smtp.Value.IsConfigured && string.IsNullOrWhiteSpace(o.PublicUrl))
            errors.Add("Auth:PublicUrl is required when Smtp is configured (it is used in e-mailed links).");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
