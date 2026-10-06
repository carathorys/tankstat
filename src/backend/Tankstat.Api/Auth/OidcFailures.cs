using System.Diagnostics;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;

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

/// <summary>
/// A refusal of our own in <c>OnTokenValidated</c>, carried by code so the failure handler classifies it without reading any text. It also
/// carries the page the sign-in meant to return to: a failure raised in that event reaches <c>OnRemoteFailure</c> without the
/// authentication properties, so the handler could not know the page otherwise.
/// </summary>
internal sealed class OidcSignInRefusedException(OidcFailure reason, string? returnUrl = null, Exception? cause = null) : Exception(reason.ToString(), cause)
{
    public OidcFailure Reason { get; } = reason;
    public string? ReturnUrl { get; } = returnUrl;
}

internal static class OidcFailures
{
    public static OidcFailure Classify(Exception? failure) => failure switch
    {
        OidcSignInRefusedException e => e.Reason,
        ForbiddenException { Key: AuthService.AccountDisabledKey } => OidcFailure.AccountDisabled,
        // A stale or replayed callback fails on our side (the nonce or state does not match), before anything the provider said counts.
        OpenIdConnectProtocolInvalidNonceException or OpenIdConnectProtocolInvalidStateException => OidcFailure.CallbackRejected,
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
        OidcFailure.CallbackRejected => "callback_rejected",
        _ => throw new UnreachableException($"No code for {reason}"),
    };

    /// <summary>
    /// Where the browser lands: the page the sign-in meant to return to (checked again to be a same-site path, a fragment dropped), marked
    /// as failed.
    /// </summary>
    public static string FailedUrl(OidcFailure reason, string? returnUrl)
    {
        var path = AuthExtensions.LocalPathOrRoot(returnUrl);
        var fragment = path.IndexOf('#');
        if (fragment >= 0) path = fragment == 0 ? "/" : path[..fragment];
        return $"{path}{(path.Contains('?') ? '&' : '?')}signIn=failed&reason={Code(reason)}";
    }
}
