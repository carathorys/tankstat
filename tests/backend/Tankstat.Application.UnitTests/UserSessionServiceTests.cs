using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Domain.Users;
using Tankstat.Application.Users;

namespace Tankstat.Application.UnitTests;

/// <summary>Refresh tokens: rotation, the grace for a lost answer, reuse detection, and everything that ends a session.</summary>
public class UserSessionServiceTests
{
    private static async Task<(World W, User Alice, IssuedSessionPair First)> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var issued = await w.SessionService.IssueAsync(alice, "Firefox on Linux", default);
        return (w, alice, new IssuedSessionPair(issued.Session, issued.Token));
    }

    private sealed record IssuedSessionPair(UserSession Session, string Token);

    private static Task<UnauthenticatedException> Refused(World w, string? token) =>
        Assert.ThrowsAsync<UnauthenticatedException>(() => w.SessionService.RefreshAsync(token, default));

    [Fact]
    public async Task ARefresh_TradesTheTokenForANewOne_AndMovesTheExpiryOn()
    {
        var (w, alice, first) = await Setup();
        w.Clock.Advance(TimeSpan.FromDays(30));

        var refreshed = await w.SessionService.RefreshAsync(first.Token, default);

        Assert.Equal((alice.Id, first.Session.Id), (refreshed.User.Id, refreshed.Session.Id));
        Assert.NotEqual(first.Token, refreshed.Token);
        Assert.Equal(w.Clock.GetUtcNow() + TimeSpan.FromDays(90), refreshed.Session.ExpiresAt);
        Assert.Equal(first.Session.Id.ToString("N"), refreshed.Token.Split('.')[0]);
        Assert.DoesNotContain(refreshed.Token.Split('.')[1], first.Session.SecretHash); // only a hash is stored
    }

    [Fact]
    public async Task TheTokenItHadBefore_IsAnsweredWithTheCurrentOneForAMinute_ThenEndsTheSession()
    {
        var (w, _, first) = await Setup();
        var refreshed = await w.SessionService.RefreshAsync(first.Token, default);

        w.Clock.Advance(TimeSpan.FromSeconds(30)); // the answer got lost, or another tab refreshed at the same time
        var again = await w.SessionService.RefreshAsync(first.Token, default);
        Assert.Equal(refreshed.Token, again.Token); // the same secret, so both tabs keep working

        w.Clock.Advance(TimeSpan.FromMinutes(5));
        await Refused(w, first.Token); // an old secret, long after: someone else has a copy
        Assert.NotNull(first.Session.RevokedAt);
        await Refused(w, refreshed.Token); // the session is over for everyone
        Assert.Contains(w.Log.From<UserSessionService>(), e => e.Level == LogLevel.Warning && e.Values["SessionId"]?.ToString() == first.Session.Id.ToString());
    }

    [Fact]
    public async Task ARefreshThatLostTheRaceToRotate_IsAnsweredWithTheSecretTheOtherSaved()
    {
        var (w, _, first) = await Setup();
        w.Sessions.TwinRotatesNext = true; // another tab, presenting the same secret, rotated it a moment before this one saved

        var refreshed = await w.SessionService.RefreshAsync(first.Token, default);

        Assert.Null(first.Session.RevokedAt);
        var next = await w.SessionService.RefreshAsync(refreshed.Token, default); // what this tab got works
        Assert.Equal(first.Session.Id, next.Session.Id);
    }

    [Fact]
    public async Task AnExpiredSession_AWrongSecret_OrRubbish_AreRefused_AllAlike()
    {
        var (w, _, first) = await Setup();

        await Refused(w, null);
        await Refused(w, "not-a-token");
        await Refused(w, $"{first.Session.Id:N}.wrong-secret");
        w.Clock.Advance(TimeSpan.FromDays(91));
        await Refused(w, first.Token);
        Assert.All(w.Log.Entries, e => Assert.DoesNotContain(first.Token.Split('.')[1], e.Message)); // never the secret
    }

    [Fact]
    public async Task APasswordChange_KeepsTheDeviceThatAsked_AndEndsTheOthers()
    {
        var (w, alice, first) = await Setup();
        var phone = await w.SessionService.IssueAsync(alice, "Safari on iOS", default);
        w.Current.SignInAs(alice);

        await w.Auth.ChangePasswordAsync("password-123456", "a-new-password-1", default, first.Session.Id);

        Assert.Equal(alice.SessionVersion, first.Session.SessionVersion); // took the new version, so its next refresh works
        Assert.NotNull(await w.SessionService.RefreshAsync(first.Token, default));
        Assert.NotNull(phone.Session.RevokedAt);
        await Refused(w, phone.Token);
    }

    [Fact]
    public async Task AResetPassword_ADisabledUser_AndAnAdministratorSettingThePassword_EndEverySession()
    {
        var w = new World(configure: o => o.Standalone.AllowAdminSetPassword = true);
        var admin = w.AddUser("admin@x.co", admin: true);
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        var aliceSession = await w.SessionService.IssueAsync(alice, null, default);
        var bobSession = await w.SessionService.IssueAsync(bob, null, default);
        w.Current.SignInAs(admin);

        await w.UserService.SetPasswordAsync(alice.Id, "set-by-the-admin-1", default);
        await w.UserService.SetDisabledAsync(bob.Id, true, default);

        Assert.NotNull(aliceSession.Session.RevokedAt);
        Assert.NotNull(bobSession.Session.RevokedAt);
    }

    [Fact]
    public async Task ASessionFromBeforeAPasswordChangeElsewhere_IsRefused_EvenUnrevoked()
    {
        var (w, alice, first) = await Setup();
        alice.SetPasswordHash("changed somewhere the sessions were not told");

        await Refused(w, first.Token);

        Assert.NotNull(first.Session.RevokedAt);
    }

    [Fact]
    public async Task SigningOut_EndsOnlyTheDevicesOwnSession_AndOnlyWithItsSecret()
    {
        var (w, alice, first) = await Setup();
        var other = await w.SessionService.IssueAsync(alice, null, default);

        Assert.Null(await w.SessionService.RevokeByTokenAsync($"{first.Session.Id:N}.guessed", default));
        Assert.Equal(first.Session.Id, await w.SessionService.RevokeByTokenAsync(first.Token, default));

        Assert.NotNull(first.Session.RevokedAt);
        Assert.Null(other.Session.RevokedAt);
        Assert.Null(await w.SessionService.RevokeAsync(other.Session.Id, Guid.NewGuid(), default)); // not the user's own
        Assert.Null(other.Session.RevokedAt);
    }

    [Fact]
    public async Task APerson_SeesTheirOwnDevices_AndSignsThemOut()
    {
        var (w, alice, laptop) = await Setup();
        var bob = w.AddUser("bob@x.co");
        w.Clock.Advance(TimeSpan.FromHours(1));
        var phone = await w.SessionService.IssueAsync(alice, "Safari on iOS", default);
        var bobs = await w.SessionService.IssueAsync(bob, null, default);
        var ended = await w.SessionService.IssueAsync(alice, null, default);
        await w.SessionService.RevokeByTokenAsync(ended.Token, default);
        w.Current.SignInAs(alice);

        Assert.Equal([phone.Session.Id, laptop.Session.Id], (await w.SessionService.ListMineAsync(default)).Select(s => s.Id)); // most recently used first

        Assert.False(await w.SessionService.RevokeMineAsync(bobs.Session.Id, default)); // not hers
        Assert.True(await w.SessionService.RevokeMineAsync(phone.Session.Id, default));
        Assert.False(await w.SessionService.RevokeMineAsync(phone.Session.Id, default)); // already ended
        Assert.Null(bobs.Session.RevokedAt);
        Assert.Equal([laptop.Session.Id], (await w.SessionService.ListMineAsync(default)).Select(s => s.Id));
    }

    [Fact]
    public async Task SigningOutEverywhereElse_KeepsThisDevice()
    {
        var (w, alice, laptop) = await Setup();
        await w.SessionService.IssueAsync(alice, null, default);
        await w.SessionService.IssueAsync(alice, null, default);
        w.Current.SignInAs(alice);

        Assert.Equal(2, await w.SessionService.RevokeMyOthersAsync(laptop.Session.Id, default));

        Assert.Equal([laptop.Session.Id], (await w.SessionService.ListMineAsync(default)).Select(s => s.Id));
    }

    [Fact]
    public async Task WithoutSessions_TheListIsNotAvailable()
    {
        var w = new World(AuthMode.ProxyHeader);
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        Assert.Equal("auth.modeUnavailable", (await Assert.ThrowsAsync<Tankstat.Domain.DomainException>(() => w.SessionService.ListMineAsync(default))).Key);
    }

    [Fact]
    public async Task EndedSessions_AreDeletedAWeekLater_WhenSomeoneRefreshes()
    {
        var (w, alice, first) = await Setup();
        var kept = await w.SessionService.IssueAsync(alice, null, default);
        await w.SessionService.RevokeByTokenAsync(first.Token, default);

        w.Clock.Advance(TimeSpan.FromDays(8));
        await w.SessionService.RefreshAsync(kept.Token, default);

        Assert.Equal([kept.Session.Id], w.Sessions.Items.Select(s => s.Id));
    }
}
