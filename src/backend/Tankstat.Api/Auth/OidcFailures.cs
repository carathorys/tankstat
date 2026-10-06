using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Tankstat.Application.Auth;

namespace Tankstat.Api.Auth;

/// <summary>Why an OIDC sign-in ended without a session. A code, never text: it is what the log says and what the browser gets (<c>?reason=</c>).</summary>
internal enum OidcFailure
{
    /// <summary>The provider answered with an error (at the authorize or the token endpoint).</summary>
    ProviderError,

    /// <summary>The user cancelled, or the provider refused the request.</summary>
    AccessDenied,

    /// <summary>The token did not validate (signature, issuer, lifetime).</summary>
    TokenRejected,

    /// <summary>The provider returned no subject.</summary>
    NoSubject,

    /// <summary>The user exists here but is disabled.</summary>
    AccountDisabled,

    /// <summary>The callback could not be tied to a sign-in this server started (state, correlation cookie, unreachable token endpoint).</summary>
    CallbackRejected,
}

/// <summary>A refusal of our own in <c>OnTokenValidated</c>, carried by code so the failure handler classifies it without reading any text.</summary>
internal sealed class OidcSignInRefusedException(OidcFailure reason) : Exception(reason.ToString())
{
    public OidcFailure Reason { get; } = reason;
}

internal static class OidcFailures
{
    public static OidcFailure Classify(Exception? failure) => failure switch
    {
        OidcSignInRefusedException e => e.Reason,
        ForbiddenException { Key: "auth.accountDisabled" } => OidcFailure.AccountDisabled,
        OpenIdConnectProtocolException => OidcFailure.ProviderError,
        SecurityTokenException => OidcFailure.TokenRejected,
        _ => OidcFailure.CallbackRejected,
    };

    /// <summary>The snake_case code the browser and the log get.</summary>
    public static string Code(OidcFailure reason) => reason switch
    {
        OidcFailure.ProviderError => "provider_error",
        OidcFailure.AccessDenied => "access_denied",
        OidcFailure.TokenRejected => "token_rejected",
        OidcFailure.NoSubject => "no_subject",
        OidcFailure.AccountDisabled => "account_disabled",
        _ => "callback_rejected",
    };

    /// <summary>Where the browser lands: the page the sign-in meant to return to (checked again to be a same-site path), marked as failed.</summary>
    public static string FailedUrl(OidcFailure reason, string? returnUrl)
    {
        var path = AuthExtensions.LocalPathOrRoot(returnUrl);
        return $"{path}{(path.Contains('?') ? '&' : '?')}signIn=failed&reason={Code(reason)}";
    }
}
