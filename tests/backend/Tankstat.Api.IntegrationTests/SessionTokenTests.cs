using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Tankstat.Api.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// A short access cookie and a refresh cookie: a device stays signed in by trading its refresh token for a new access cookie at
/// <c>POST /auth/token/refresh</c>; the server ends sessions on sign-out, password changes, disabling, and a refresh token used twice.
/// </summary>
public sealed class SessionTokenTests : IDisposable
{
    private const string Admin = "root@example.com";
    private const string AdminPassword = "initial-password-1";
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly TestApp _app;

    public SessionTokenTests() => _app = TestApp.Standalone(services: s => s.AddSingleton<TimeProvider>(_clock));

    public void Dispose() => _app.Dispose();

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string? cookie = null, bool header = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (header) request.Headers.Add("X-Requested-With", "fetch");
        if (cookie is not null) request.Headers.Add("Cookie", $"tankstat.refresh={cookie}");
        return c.SendAsync(request);
    }

    /// <summary>The refresh token a response set (for the tests that play a thief with a copy).</summary>
    private static string RefreshToken(HttpResponseMessage response)
    {
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("tankstat.refresh="));
        return cookie["tankstat.refresh=".Length..cookie.IndexOf(';')];
    }

    private async Task<(HttpClient Client, string Token)> SignIn()
    {
        var c = _app.NewClient();
        var response = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation($i: LoginInput!) { login(input: $i) { id } }",
            variables = new { i = new { email = Admin, password = AdminPassword } },
        });
        return (c, RefreshToken(response));
    }

    private HttpClient WithoutCookies() => _app.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<bool> SignedIn(HttpClient c) => (await c.Gql("{ myVehicles { id } }")).ErrorCode() is null;

    [Fact]
    public async Task SigningIn_SetsAShortAccessCookie_AndARefreshCookieOnlyForTheTokenEndpoints()
    {
        var response = await _app.NewClient().PostAsJsonAsync("/graphql", new
        {
            query = "mutation($i: LoginInput!) { login(input: $i) { id } }",
            variables = new { i = new { email = Admin, password = AdminPassword } },
        });

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var access = Assert.Single(cookies, h => h.StartsWith("tankstat.session="));
        var refresh = Assert.Single(cookies, h => h.StartsWith("tankstat.refresh="));
        Assert.Contains("expires=", access, StringComparison.OrdinalIgnoreCase); // persistent: closing the browser does not sign out
        Assert.Contains("httponly", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/auth/token", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", refresh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhenTheAccessCookieRunsOut_ARefreshSignsTheDeviceInAgain()
    {
        var (c, _) = await SignIn();
        Assert.True(await SignedIn(c));

        _clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ myVehicles { id } }")).ErrorCode());

        Assert.Equal(HttpStatusCode.NoContent, (await Post(c, "/auth/token/refresh")).StatusCode);
        Assert.True(await SignedIn(c));

        _clock.Advance(TimeSpan.FromDays(60)); // used now and then, a device stays signed in
        Assert.Equal(HttpStatusCode.NoContent, (await Post(c, "/auth/token/refresh")).StatusCode);
        Assert.True(await SignedIn(c));
    }

    [Fact]
    public async Task ARefreshWithoutTheHeader_IsRefused()
    {
        var (c, _) = await SignIn();

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/auth/token/refresh", header: false)).StatusCode);
    }

    [Fact]
    public async Task ARefreshTokenUsedAgainLater_EndsTheSession_ForEveryCopy()
    {
        var (c, stolen) = await SignIn();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(c, "/auth/token/refresh")).StatusCode); // the device moved on

        _clock.Advance(TimeSpan.FromMinutes(5));
        var thief = await Post(WithoutCookies(), "/auth/token/refresh", stolen);

        Assert.Equal(HttpStatusCode.Unauthorized, thief.StatusCode);
        Assert.Contains("auth.unauthenticated", await thief.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, "/auth/token/refresh")).StatusCode);
    }

    [Fact]
    public async Task TabsRefreshingWithTheSameToken_WithinTheGrace_AllGetTheCurrentToken_WithoutANewRotation()
    {
        var (_, first) = await SignIn();
        var rotated = await Post(WithoutCookies(), "/auth/token/refresh", first); // one tab trades the token in
        var current = RefreshToken(rotated);

        _clock.Advance(TimeSpan.FromSeconds(30)); // another tab, a moment later, with the token the first one traded in
        var late = await Post(WithoutCookies(), "/auth/token/refresh", first);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (rotated.StatusCode, late.StatusCode));
        Assert.NotEqual(first, current);
        Assert.Equal(current, RefreshToken(late)); // the same secret again: the tabs converge on one token
        Assert.Equal(HttpStatusCode.NoContent, (await Post(WithoutCookies(), "/auth/token/refresh", current)).StatusCode);
    }

    [Fact]
    public async Task ASessionIsNamedByItsBrowserAndSystem_NeverByTheRawUserAgent()
    {
        var c = _app.NewClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0");
        await c.LoginAs(Admin, AdminPassword);

        var body = await c.Gql(MySessions);

        Assert.Equal("Firefox on Linux", Assert.Single(body.Data().GetProperty("mySessions").EnumerateArray()).GetProperty("client").GetString());
        Assert.DoesNotContain("rv:131.0", body.GetRawText());
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 Edg/120.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 OPR/106.0", "Opera on macOS")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0 Mobile/15E148 Safari/604.1", "Chrome on iOS")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15", "Safari on macOS")]
    [InlineData("Firefox/131.0", "Firefox")] // a browser on a system it does not name
    [InlineData("SomeApp/1.0 (Windows NT 10.0)", "Windows")] // a system, and no browser it knows
    [InlineData("curl/8.5.0", null)]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void ClientLabel_IsACoarseNameForTheDevice(string? userAgent, string? label) => Assert.Equal(label, ClientLabel.Of(userAgent));

    [Fact]
    public void AStoredSecret_CannotBeReadBack_OnceTheKeyRingIsReplaced()
    {
        var before = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var after = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var stored = before.Protect("the-secret");

        Assert.NotEqual("the-secret", stored);
        Assert.Equal("the-secret", before.Unprotect(stored));
        Assert.Null(after.Unprotect(stored)); // the device signs in again, nothing throws
    }

    [Fact]
    public void TheSessionOfAnAccessCookie_IsItsSidClaim_AndNoneForACookieFromBeforeSessions()
    {
        var id = Guid.NewGuid();
        ClaimsPrincipal With(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

        Assert.Equal(id, SessionCookies.SessionId(With(new Claim(SessionClaims.SessionClaim, id.ToString()))));
        Assert.Null(SessionCookies.SessionId(With(new Claim(SessionClaims.SessionClaim, "not-a-guid"))));
        Assert.Null(SessionCookies.SessionId(With()));
        Assert.Null(SessionCookies.SessionId(null));
    }

    [Fact]
    public async Task SigningOut_EndsTheSession_EvenWithTheAccessCookieRunOut()
    {
        var (c, token) = await SignIn();
        _clock.Advance(TimeSpan.FromMinutes(16));

        var response = await Post(c, "/auth/token/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("tankstat.refresh=;"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(WithoutCookies(), "/auth/token/refresh", token)).StatusCode);
    }

    [Fact]
    public async Task AnEndedSession_EndsItsAccessCookieAtOnce()
    {
        var c = _app.NewClient();
        var login = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation($i: LoginInput!) { login(input: $i) { id } }",
            variables = new { i = new { email = Admin, password = AdminPassword } },
        });
        var header = Assert.Single(login.Headers.GetValues("Set-Cookie"), h => h.StartsWith("tankstat.session="));
        var copy = WithoutCookies();
        copy.DefaultRequestHeaders.Add("Cookie", header[..header.IndexOf(';')]); // the access cookie, kept by someone
        Assert.True(await SignedIn(copy));

        await Post(c, "/auth/token/logout");

        Assert.Equal("UNAUTHENTICATED", (await copy.Gql("{ myVehicles { id } }")).ErrorCode()); // not only after it runs out
    }

    [Fact]
    public async Task TheGraphQLSignOut_EndsTheSessionToo()
    {
        var (c, token) = await SignIn();

        Assert.True((await c.Gql("mutation { logout }")).Data().GetProperty("logout").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(WithoutCookies(), "/auth/token/refresh", token)).StatusCode);
    }

    [Fact]
    public async Task AChangedPassword_KeepsThisDevice_AndEndsTheOthers()
    {
        var (laptop, _) = await SignIn();
        var (phone, _) = await SignIn();

        (await laptop.Gql("mutation($i: ChangePasswordInput!) { changePassword(input: $i) }",
            new { i = new { currentPassword = AdminPassword, newPassword = "a-brand-new-password" } })).Data();
        _clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.NoContent, (await Post(laptop, "/auth/token/refresh")).StatusCode);
        Assert.True(await SignedIn(laptop));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(phone, "/auth/token/refresh")).StatusCode);
    }

    [Fact]
    public async Task ADisabledUser_CannotRefresh()
    {
        var users = await _app.Users();
        (await users.Admin.Gql("mutation($id: UUID!) { setUserDisabled(userId: $id, disabled: true) { id } }", new { id = users.AliceId })).Data();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(users.Alice, "/auth/token/refresh")).StatusCode);
    }

    private const string MySessions = "{ mySessions { id client current createdAt lastUsedAt expiresAt } }";

    [Fact]
    public async Task TheAccountPage_ListsTheDevices_AndMarksThisOne()
    {
        var (laptop, _) = await SignIn();
        var (phone, _) = await SignIn();

        var listed = (await laptop.Gql(MySessions)).Data().GetProperty("mySessions").EnumerateArray().ToList();

        Assert.Equal(2, listed.Count);
        Assert.Single(listed, s => s.GetProperty("current").GetBoolean());
        Assert.True(await SignedIn(phone));
    }

    [Fact]
    public async Task SigningAnotherDeviceOut_EndsItsSession_AndSigningOutThisOne_RemovesTheCookies()
    {
        var (laptop, _) = await SignIn();
        var (phone, phoneToken) = await SignIn();
        var sessions = (await laptop.Gql(MySessions)).Data().GetProperty("mySessions").EnumerateArray().ToList();
        var phoneId = sessions.Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetString();
        var laptopId = sessions.Single(s => s.GetProperty("current").GetBoolean()).GetProperty("id").GetString();

        Assert.True((await laptop.Gql("mutation($id: UUID!) { revokeSession(id: $id) }", new { id = phoneId })).Data().GetProperty("revokeSession").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(WithoutCookies(), "/auth/token/refresh", phoneToken)).StatusCode);

        var response = await laptop.PostAsJsonAsync("/graphql", new { query = "mutation($id: UUID!) { revokeSession(id: $id) }", variables = new { id = laptopId } });
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("tankstat.refresh=;"));
        Assert.False(await SignedIn(laptop));
    }

    [Fact]
    public async Task SigningOutEverywhereElse_KeepsThisDevice()
    {
        var (laptop, _) = await SignIn();
        var (phone, _) = await SignIn();
        var (tablet, _) = await SignIn();

        Assert.Equal(2, (await laptop.Gql("mutation { revokeOtherSessions }")).Data().GetProperty("revokeOtherSessions").GetInt32());
        _clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.NoContent, (await Post(laptop, "/auth/token/refresh")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(phone, "/auth/token/refresh")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(tablet, "/auth/token/refresh")).StatusCode);
    }

    [Fact]
    public async Task WithoutAuthentication_ThereAreNoTokenEndpoints()
    {
        using var none = new TestApp(new Dictionary<string, string?> { ["Auth:Mode"] = "None" });

        Assert.Equal(HttpStatusCode.NotFound, (await Post(none.NewClient(), "/auth/token/refresh")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(none.NewClient(), "/auth/token/logout")).StatusCode);
    }
}
