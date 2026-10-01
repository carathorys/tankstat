using Microsoft.Extensions.Options;

namespace Tankstat.Application.Auth;

public sealed class AuthOptionsValidator(IOptions<SmtpOptions> smtp) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions o)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(o.Mode)) errors.Add($"Auth:Mode '{o.Mode}' is not supported.");

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
