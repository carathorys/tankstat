using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Tankstat.Api.Auth;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;

namespace Tankstat.Api.IntegrationTests;

/// <summary>A failed OIDC sign-in ends as a code (for the log and the browser), never as the provider's text.</summary>
public class OidcFailuresTests
{
    [Fact]
    public void Classify_ByTheKindOfException()
    {
        Assert.Equal(OidcFailure.NoSubject, OidcFailures.Classify(new OidcSignInRefusedException(OidcFailure.NoSubject)));
        Assert.Equal(OidcFailure.AccountDisabled, OidcFailures.Classify(new ForbiddenException(AuthService.AccountDisabledKey, "This account has been disabled.")));
        Assert.Equal(OidcFailure.CallbackRejected, OidcFailures.Classify(new ForbiddenException()));
        Assert.Equal(OidcFailure.ProviderError, OidcFailures.Classify(new OpenIdConnectProtocolException("server_error")));
        Assert.Equal(OidcFailure.CallbackRejected, OidcFailures.Classify(new OpenIdConnectProtocolInvalidNonceException("nonce"))); // ours to check, not the provider's error
        Assert.Equal(OidcFailure.CallbackRejected, OidcFailures.Classify(new OpenIdConnectProtocolInvalidStateException("state")));
        Assert.Equal(OidcFailure.TokenRejected, OidcFailures.Classify(new SecurityTokenException("bad signature")));
        Assert.Equal(OidcFailure.CallbackRejected, OidcFailures.Classify(new AuthenticationFailureException("Unable to unprotect the message.State.")));
        Assert.Equal(OidcFailure.CallbackRejected, OidcFailures.Classify(null));
    }

    [Theory] // the enum is internal to the API, so the theory names its values
    [InlineData("ProviderError", "provider_error")]
    [InlineData("AccessDenied", "access_denied")]
    [InlineData("TokenRejected", "token_rejected")]
    [InlineData("NoSubject", "no_subject")]
    [InlineData("AccountDisabled", "account_disabled")]
    [InlineData("CallbackRejected", "callback_rejected")]
    public void Codes_AreStableSnakeCase(string reason, string code) => Assert.Equal(code, OidcFailures.Code(Enum.Parse<OidcFailure>(reason)));

    [Fact]
    public void AReasonWithoutACode_IsABug_NeverAnEmptyCodeInTheUrl() =>
        Assert.Throws<System.Diagnostics.UnreachableException>(() => OidcFailures.Code((OidcFailure)99));

    [Theory]
    [InlineData(null, "/?signIn=failed&reason=access_denied")]
    [InlineData("/", "/?signIn=failed&reason=access_denied")]
    [InlineData("/vehicles/abc", "/vehicles/abc?signIn=failed&reason=access_denied")]
    [InlineData("/a?b=c", "/a?b=c&signIn=failed&reason=access_denied")]
    [InlineData("//evil.example.com", "/?signIn=failed&reason=access_denied")]
    [InlineData("https://evil.example.com", "/?signIn=failed&reason=access_denied")]
    [InlineData("/\t/evil.example.com", "/?signIn=failed&reason=access_denied")]
    [InlineData("/vehicles/abc#top", "/vehicles/abc?signIn=failed&reason=access_denied")] // the marker never hides behind a fragment
    [InlineData("/#x", "/?signIn=failed&reason=access_denied")]
    public void FailedUrl_StaysOnThisSite_AndMarksTheFailure(string? returnUrl, string expected) =>
        Assert.Equal(expected, OidcFailures.FailedUrl(OidcFailure.AccessDenied, returnUrl));
}
