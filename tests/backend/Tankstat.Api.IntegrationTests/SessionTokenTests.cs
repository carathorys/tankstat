using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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

    [Fact]
    public async Task WithoutAuthentication_ThereAreNoTokenEndpoints()
    {
        using var none = new TestApp(new Dictionary<string, string?> { ["Auth:Mode"] = "None" });

        Assert.Equal(HttpStatusCode.NotFound, (await Post(none.NewClient(), "/auth/token/refresh")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(none.NewClient(), "/auth/token/logout")).StatusCode);
    }
}
