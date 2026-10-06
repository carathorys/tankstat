using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Tankstat.Api.Auth;
using Tankstat.Application.Users;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The whole OIDC sign-in without a real provider: the token endpoint is a stub that redeems any code for an id_token signed by a test key
/// the static configuration trusts. This is where the server's own refusals (OnTokenValidated, ctx.Fail, OnRemoteFailure) actually run.
/// </summary>
[Collection(ApiCollection.Name)]
public class OidcProviderFlowTests : IDisposable
{
    private const string Issuer = "https://id.example.com";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("tankstat-test-signing-key-with-32-plus-bytes!"));
    private readonly TokenEndpointStub _tokenEndpoint = new();
    private readonly TestApp _app;

    public OidcProviderFlowTests()
    {
        _app = new TestApp(
            new() { ["Auth:Mode"] = "Oidc", ["Auth:Oidc:Authority"] = Issuer, ["Auth:Oidc:ClientId"] = "tankstat", ["Auth:Oidc:ClientSecret"] = "secret" },
            s => s.PostConfigure<OpenIdConnectOptions>(SessionClaims.OidcScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer, AuthorizationEndpoint = $"{Issuer}/authorize", TokenEndpoint = $"{Issuer}/token" };
                configuration.SigningKeys.Add(Key);
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                o.GetClaimsFromUserInfoEndpoint = false;
                o.Backchannel = new HttpClient(_tokenEndpoint);
            }));
    }

    public void Dispose() => _app.Dispose();

    /// <summary>A fresh browser starts a sign-in; the provider would now ask the user and send them back with a code.</summary>
    private async Task<(HttpClient Browser, string State, string Nonce)> StartAsync()
    {
        var browser = _app.NewHttpsClientWithoutRedirects(); // the correlation and nonce cookies are Secure
        var challenge = await browser.GetAsync("/auth/oidc/login?returnUrl=/vehicles/abc");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        return (browser, query["state"].ToString(), query["nonce"].ToString());
    }

    /// <summary>The provider sends the browser back with a code, and redeeming it yields an id_token for <paramref name="subject"/>.</summary>
    private async Task<HttpResponseMessage> ComeBackAsync((HttpClient Browser, string State, string Nonce) signIn, string? subject)
    {
        _tokenEndpoint.NextIdToken = IdToken(signIn.Nonce, subject);
        return await signIn.Browser.GetAsync($"/auth/oidc/callback?code=the-code&state={Uri.EscapeDataString(signIn.State)}");
    }

    private static string IdToken(string nonce, string? subject)
    {
        var claims = new List<Claim> { new("nonce", nonce), new("email", "alice@example.com"), new("email_verified", "true"), new("name", "Alice") };
        if (subject is not null) claims.Add(new Claim("sub", subject));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor // iat, nbf and exp are filled in
        {
            Issuer = Issuer,
            Audience = "tankstat",
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }

    private IEnumerable<TestSupport.LogEntry> Outcomes() => _app.Log.From(typeof(AuthExtensions).FullName!).Where(e => e.Level == LogLevel.Warning);

    [Fact]
    public async Task TheFirstSignIn_CreatesTheUser_AndReturnsToTheirPage()
    {
        var signIn = await StartAsync();

        var back = await ComeBackAsync(signIn, "subject-1");

        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.Equal("/vehicles/abc", back.Headers.Location!.ToString());
        var user = (await signIn.Browser.Gql("{ session { user { email displayName } } }")).Data().GetProperty("session").GetProperty("user");
        Assert.Equal(("alice@example.com", "Alice"), (user.GetProperty("email").GetString(), user.GetProperty("displayName").GetString()));
        Assert.Empty(Outcomes());
    }

    [Fact]
    public async Task ADisabledUser_LandsOnTheFailedScreen_WithTheReason_AndTheLogSaysOnlyTheCode()
    {
        var first = await StartAsync();
        await ComeBackAsync(first, "subject-1");
        var id = Guid.Parse((await first.Browser.Gql("{ session { user { id } } }")).Data().GetProperty("session").GetProperty("user").GetProperty("id").GetString()!);
        await using (var scope = _app.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = (await users.FindByIdAsync(id, default))!;
            user.SetDisabled(true);
            await users.UpdateAsync(user, default);
        }

        var again = await StartAsync();
        var refused = await ComeBackAsync(again, "subject-1");

        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal("/vehicles/abc?signIn=failed&reason=account_disabled", refused.Headers.Location!.ToString());
        Assert.Equal("account_disabled", Assert.Single(Outcomes()).Values["Reason"]);
        Assert.DoesNotContain(_app.Log.From("Tankstat"), e => e.Text.Contains("subject-1") || e.Text.Contains("alice@example.com")); // ids only, never the subject or the address
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await again.Browser.Gql("{ session { user { id } } }")).Data().GetProperty("session").GetProperty("user").ValueKind);
    }

    [Fact]
    public async Task AnIdTokenWithoutASubject_IsRefused_WithTheReason()
    {
        var signIn = await StartAsync();

        var refused = await ComeBackAsync(signIn, subject: null);

        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal("/vehicles/abc?signIn=failed&reason=no_subject", refused.Headers.Location!.ToString());
        Assert.Equal("no_subject", Assert.Single(Outcomes()).Values["Reason"]);
    }

    /// <summary>The provider's token endpoint: redeems any code for the id_token the test prepared (an access token must come with it).</summary>
    private sealed class TokenEndpointStub : HttpMessageHandler
    {
        public string? NextIdToken { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal($"{Issuer}/token", request.RequestUri!.GetLeftPart(UriPartial.Path));
            var body = $$"""{"id_token":"{{NextIdToken}}","access_token":"the-access-token","token_type":"Bearer"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
