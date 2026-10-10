using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Tankstat.Api.Auth;

namespace Tankstat.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class NoAuthModeTests : IDisposable
{
    private readonly TestApp _app = new(new() { ["Auth:Mode"] = "None" });

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Session_ReportsNoAuth_AndTheUiWarningNotice()
    {
        var data = (await _app.NewClient().Gql("{ session { mode user { id } } notices { code severity message } }")).Data();

        Assert.Equal("NONE", data.GetProperty("session").GetProperty("mode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("session").GetProperty("user").ValueKind);
        var notice = Assert.Single(data.GetProperty("notices").EnumerateArray());
        Assert.Equal("AUTH_DISABLED", notice.GetProperty("code").GetString());
        Assert.Equal("WARNING", notice.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Everyone_SeesAndChangesEverything()
    {
        var one = _app.NewClient();
        var two = _app.NewClient();
        await one.Gql("mutation { addVehicle(input: { name: \"Shared\", fuelType: PETROL }) { id } }");

        var seen = (await two.Gql("{ myVehicles { name } }")).Data().GetProperty("myVehicles");

        Assert.Equal("Shared", Assert.Single(seen.EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task LoginAndAdministratorFeatures_AreNotAvailable()
    {
        var c = _app.NewClient();

        Assert.Equal("VALIDATION_FAILED", (await c.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }",
            new { i = new { email = "a@example.com", password = "whatever-password" } })).ErrorCode());
        Assert.Equal("FORBIDDEN", (await c.Gql("{ users { id } }")).ErrorCode());
        Assert.Equal("FORBIDDEN", (await c.Gql("mutation { setDefaultAccess(level: EDIT) { defaultLevelForOthers } }")).ErrorCode());
    }

    [Fact]
    public async Task CookiesFromAnotherMode_CountForNothing()
    {
        using var data = new SharedData();
        var cookies = await data.StandaloneAdminCookiesAsync(); // an administrator signed in before the restart in mode None
        static async Task<string?> UserEmail(HttpClient c)
        {
            var user = (await c.Gql("{ session { user { email } } }")).Data().GetProperty("session").GetProperty("user");
            return user.ValueKind == JsonValueKind.Null ? null : user.GetProperty("email").GetString();
        }
        using (var standalone = data.Host(TestApp.StandaloneSettings())) // the control: restarted in Standalone, the same cookies still name the administrator
        {
            Assert.Equal(TestApp.AdminEmail, await UserEmail(standalone.NewClientHolding(cookies)));
            Assert.Contains(standalone.Log.Entries, e => e.ScopeText.Contains("UserId=")); // and the request ran under the administrator's log scope
        }

        using var none = data.Host(new() { ["Auth:Mode"] = "None" });
        var client = none.NewClientHolding(cookies);
        Assert.Null(await UserEmail(client));
        Assert.Equal("FORBIDDEN", (await client.Gql("{ users { id } }")).ErrorCode());
        Assert.Equal("FORBIDDEN", (await client.Gql("{ vehicles { id } }")).ErrorCode());
        var added = (await client.Gql("mutation { addVehicle(input: { name: \"Leftover\", fuelType: PETROL }) { ownerId } }")).Data();
        Assert.Equal(Guid.Empty.ToString(), added.GetProperty("addVehicle").GetProperty("ownerId").GetString()); // the anonymous owner's

        // logout still deletes both cookies (it names its scheme), though no screen offers it in mode None and no session is named to end
        var logout = await client.PostAsJsonAsync("/graphql", new { query = "mutation { logout }" });
        var set = logout.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(set, h => h.StartsWith("tankstat.session=;"));
        Assert.Contains(set, h => h.StartsWith("tankstat.refresh=;"));
        Assert.DoesNotContain(none.Log.Entries, e => e.ScopeText.Contains("UserId=")); // no request ran as the administrator
    }
}

public sealed class FakeRemoteIpStartupFilter : IStartupFilter
{
    /// <summary>TestServer has no remote address; tests choose one with this header.</summary>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nextMiddleware) =>
        {
            if (ctx.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ip)) ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
            return nextMiddleware();
        });
        next(app);
    };
}

[Collection(ApiCollection.Name)]
public class ProxyHeaderModeTests : IDisposable
{
    private const string ProxyHandler = "Tankstat.Api.Auth.ProxyHeaderAuthenticationHandler";

    private readonly TestApp _app = new(
        new()
        {
            ["Auth:Mode"] = "ProxyHeader",
            ["Auth:ProxyHeader:TrustedProxies:0"] = "10.0.0.0/8",
            ["Auth:AdminEmails:0"] = "boss@example.com",
        },
        s => s.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>());

    public void Dispose() => _app.Dispose();

    private HttpClient Client(string? user, string? email = null, string remoteIp = "10.1.2.3") => WithProxy(_app.NewClient(), user, email, remoteIp);

    /// <summary>What a request through the proxy carries: the address it comes from (see <see cref="FakeRemoteIpStartupFilter"/>) and the user's headers.</summary>
    private static HttpClient WithProxy(HttpClient c, string? user, string? email, string remoteIp = "10.1.2.3")
    {
        c.DefaultRequestHeaders.Add("X-Test-Remote-Ip", remoteIp);
        if (user is not null) c.DefaultRequestHeaders.Add("X-Forwarded-User", user);
        if (email is not null) c.DefaultRequestHeaders.Add("X-Forwarded-Email", email);
        return c;
    }

    private static async Task<string?> SessionEmail(HttpClient c)
    {
        var user = (await c.Gql("{ session { user { email displayName } } }")).Data().GetProperty("session").GetProperty("user");
        return user.ValueKind == System.Text.Json.JsonValueKind.Null ? null : user.GetProperty("email").GetString();
    }

    [Fact]
    public async Task CookiesFromAnotherMode_NameNobody_TheProxysUserCounts()
    {
        using var data = new SharedData();
        var cookies = await data.StandaloneAdminCookiesAsync(); // an administrator signed in before the restart in ProxyHeader mode
        using var proxied = data.Host(new()
        {
            ["Auth:Mode"] = "ProxyHeader",
            ["Auth:ProxyHeader:TrustedProxies:0"] = "10.0.0.0/8",
        }, s => s.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>());

        Assert.Equal("alice@example.com", await SessionEmail(WithProxy(proxied.NewClientHolding(cookies), "alice", "alice@example.com")));
        Assert.Null(await SessionEmail(WithProxy(proxied.NewClientHolding(cookies), null, null))); // the cookies alone name nobody
    }

    [Fact]
    public async Task HeaderFromATrustedProxy_SignsTheUserIn()
    {
        var c = Client("alice", "alice@example.com");

        Assert.Equal("alice@example.com", await SessionEmail(c));
        Assert.Null((await c.Gql("{ myVehicles { id } }")).ErrorCode());
    }

    [Fact]
    public async Task HeaderFromAnUntrustedAddress_IsIgnored_AndSaidSo()
    {
        var c = Client("alice", "alice@example.com", remoteIp: "203.0.113.9");

        Assert.Null(await SessionEmail(c));
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ myVehicles { id } }")).ErrorCode());
        Assert.Contains(_app.Log.From(ProxyHandler), e => e.Level == LogLevel.Warning && e.Values.ContainsKey("Address"));
        Assert.False(_app.Log.Mentions("alice"));
    }

    [Fact]
    public async Task RequestWithoutTheHeader_IsAnonymous_EvenFromTheProxy()
    {
        var c = Client(null);

        Assert.Null(await SessionEmail(c));
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ myVehicles { id } }")).ErrorCode());
    }

    [Theory]
    [InlineData("alice, mallory")]
    [InlineData("  ")]
    public async Task SuspiciousHeaderValues_AreRejected(string value)
    {
        var c = Client(value);

        Assert.Null(await SessionEmail(c));
    }

    [Fact]
    public async Task ATwoUserHeader_IsLogged_ByItsNameAndNeverByItsValue()
    {
        var c = Client("alice, mallory");

        Assert.Null(await SessionEmail(c));

        Assert.Contains(_app.Log.From(ProxyHandler), e => e.Level == LogLevel.Warning && (string?)e.Values["Header"] == "X-Forwarded-User");
        Assert.False(_app.Log.Mentions("mallory")); // the name of the header, never its value
    }

    [Fact]
    public async Task ProxyUsers_AreIsolated_AndConfiguredAdministratorsSeeAll()
    {
        var alice = Client("alice", "alice@example.com");
        var bob = Client("bob", "bob@example.com");
        var boss = Client("boss", "boss@example.com");
        await alice.Gql("mutation { addVehicle(input: { name: \"Alice car\", fuelType: PETROL }) { id } }");
        await bob.Gql("mutation { addVehicle(input: { name: \"Bob car\", fuelType: PETROL }) { id } }");

        Assert.Single((await alice.Gql("{ myVehicles { name } }")).Data().GetProperty("myVehicles").EnumerateArray());
        Assert.Equal(2, (await boss.Gql("{ myVehicles { name } }")).Data().GetProperty("myVehicles").GetArrayLength());
        Assert.Null((await boss.Gql("{ users { id } }")).ErrorCode());
        Assert.Equal("FORBIDDEN", (await alice.Gql("{ users { id } }")).ErrorCode());
    }

    [Fact]
    public async Task PasswordLogin_IsNotAvailable()
    {
        var body = await Client("alice").Gql("mutation($i: LoginInput!) { login(input: $i) { id } }",
            new { i = new { email = "alice@example.com", password = "whatever-password" } });

        Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("11.0.0.1", false)]
    [InlineData("::ffff:10.1.2.3", true)] // IPv4-mapped address of a trusted proxy
    [InlineData("::1", false)]
    public void TrustedProxyMatching_UsesCidrRanges(string address, bool trusted) =>
        Assert.Equal(trusted, ProxyHeaderAuthenticationHandler.IsTrustedProxy(IPAddress.Parse(address), ["10.0.0.0/8"]));

    [Fact]
    public void TrustedProxyMatching_AcceptsBareAddresses_AndRejectsNullAndGarbage()
    {
        Assert.True(ProxyHeaderAuthenticationHandler.IsTrustedProxy(IPAddress.Parse("192.168.1.5"), ["192.168.1.5"]));
        Assert.False(ProxyHeaderAuthenticationHandler.IsTrustedProxy(IPAddress.Parse("192.168.1.6"), ["192.168.1.5"]));
        Assert.True(ProxyHeaderAuthenticationHandler.IsTrustedProxy(IPAddress.IPv6Loopback, ["::1"]));
        Assert.False(ProxyHeaderAuthenticationHandler.IsTrustedProxy(null, ["10.0.0.0/8"]));
        Assert.False(ProxyHeaderAuthenticationHandler.IsTrustedProxy(IPAddress.Loopback, ["not-an-ip", ""]));
    }
}

[Collection(ApiCollection.Name)]
public class OidcModeTests : IDisposable
{
    private readonly TestApp _app = new(
        new()
        {
            ["Auth:Mode"] = "Oidc",
            ["Auth:Oidc:Authority"] = "https://id.example.com",
            ["Auth:Oidc:ClientId"] = "tankstat",
            ["Auth:Oidc:ClientSecret"] = "secret",
        },
        // Provide the provider metadata directly so no discovery request leaves the test.
        s => s.PostConfigure<OpenIdConnectOptions>("oidc", o =>
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
            {
                Issuer = "https://id.example.com",
                AuthorizationEndpoint = "https://id.example.com/authorize",
                TokenEndpoint = "https://id.example.com/token",
            })));

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Anonymous_IsAskedToSignInThroughTheProvider()
    {
        var c = _app.NewClient();

        var data = (await c.Gql("{ session { mode user { id } } notices { code } }")).Data();

        Assert.Equal("OIDC", data.GetProperty("session").GetProperty("mode").GetString());
        Assert.Empty(data.GetProperty("notices").EnumerateArray());
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ myVehicles { id } }")).ErrorCode());
    }

    [Fact]
    public async Task LoginEndpoint_RedirectsToTheProvider_WithCodeFlowAndPkce()
    {
        var response = await _app.NewClientWithoutRedirects().GetAsync("/auth/oidc/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://id.example.com/authorize?", location);
        Assert.Contains("client_id=tankstat", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("code_challenge=", location);
        Assert.Contains("code_challenge_method=S256", location);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%2Fauth%2Foidc%2Fcallback", location);
        Assert.Contains("scope=openid profile email", Uri.UnescapeDataString(location.Replace('+', ' ')));
    }

    [Fact]
    public async Task PasswordLogin_IsNotAvailable()
    {
        var body = await _app.NewClient().Gql("mutation($i: LoginInput!) { login(input: $i) { id } }",
            new { i = new { email = "a@example.com", password = "whatever-password" } });

        Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
    }

    [Fact]
    public async Task ReturnUrl_TravelsInTheProtectedState()
    {
        var response = await _app.NewClientWithoutRedirects().GetAsync("/auth/oidc/login?returnUrl=/vehicles/abc");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var state = QueryHelpers.ParseQuery(response.Headers.Location!.Query)["state"].ToString();
        var options = _app.Factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(SessionClaims.OidcScheme);
        Assert.Equal("/vehicles/abc", options.StateDataFormat.Unprotect(state)!.RedirectUri);
    }

    [Fact]
    public async Task CallbackWithGarbageState_LandsOnTheFailedScreen_WithoutEchoingIt()
    {
        var response = await _app.NewClientWithoutRedirects().GetAsync("/auth/oidc/callback?code=x&state=garbage-7f3a");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?signIn=failed&reason=callback_rejected", response.Headers.Location!.ToString());
        var warning = Assert.Single(_app.Log.From(typeof(AuthExtensions).FullName!), e => e.Level == LogLevel.Warning);
        Assert.Equal("callback_rejected", warning.Values["Reason"]);
        Assert.DoesNotContain(_app.Log.From("Tankstat"), e => e.Text.Contains("garbage-7f3a")); // what a client sent never reaches the app's lines
    }

    [Theory]
    [InlineData("access_denied", "access_denied")]
    [InlineData("server_error", "provider_error")]
    public async Task ProviderRefusal_LandsOnTheFailedScreen_AtThePageTheUserWanted(string error, string reason)
    {
        var client = _app.NewHttpsClientWithoutRedirects(); // keeps the correlation cookie the challenge sets (Secure: only sent over HTTPS)
        var challenge = await client.GetAsync("/auth/oidc/login?returnUrl=/vehicles/abc");
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();

        var response = await client.GetAsync($"/auth/oidc/callback?error={error}&error_description=SECRET42&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/vehicles/abc?signIn=failed&reason={reason}", response.Headers.Location!.ToString());
        Assert.DoesNotContain(_app.Log.From("Tankstat"), e => e.Text.Contains("SECRET42")); // the provider's text is never logged
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/vehicles", "/vehicles")]
    [InlineData("/a?b=c", "/a?b=c")]
    [InlineData("//evil.example.com", "/")]
    [InlineData("/\\evil.example.com", "/")]
    [InlineData("https://evil.example.com", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/\t/evil.example.com", "/")]   // a browser drops the TAB and reads //evil.example.com
    [InlineData("/\t\\evil.example.com", "/")]
    [InlineData("/a\r\nX: y", "/")]              // would break the redirect header
    [InlineData("/é", "/")]
    public void ReturnUrl_MustBeALocalPath(string? input, string expected) =>
        Assert.Equal(expected, AuthExtensions.LocalPathOrRoot(input));
}

[Collection(ApiCollection.Name)]
public class OidcEndpointsInOtherModesTests : IDisposable
{
    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task OidcLoginEndpoint_DoesNotExistOutsideOidcMode()
    {
        var response = await _app.NewClientWithoutRedirects().GetAsync("/auth/oidc/login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OidcCallbackPath_IsNotHandledOutsideOidcMode()
    {
        var response = await _app.NewClientWithoutRedirects().GetAsync("/auth/oidc/callback?code=x&state=y");

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
