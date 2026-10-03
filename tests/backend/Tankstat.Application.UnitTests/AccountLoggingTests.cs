using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Sharing;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// What an operator learns about people from the log: who failed to sign in (by account id, never by the address they typed), who changed
/// a password, what an administrator did to whom, and who changed whose access. Nothing a person typed and no secret is ever in a line.
/// Lines are found by their class and level and read through the values of their placeholders, not through their wording.
/// </summary>
public class AccountLoggingTests
{
    [Fact]
    public async Task FailedSignIns_AreWarnings_ThatNameTheAccountByIdAndWhy_NeverByTheAddressTyped()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        bob.SetDisabled(true);
        var fresh = User.CreateLocal("fresh@x.co", null, false); // created by an administrator, no password chosen yet
        w.Users.Items.Add(fresh);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "wrong-password", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("ghost@x.co", "wrong-password", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("bob@x.co", "password-123456", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("fresh@x.co", "anything", default));

        var warnings = w.Log.From<AuthService>().Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Equal(4, warnings.Count);
        Assert.Equal(alice.Id, warnings[0].Values["UserId"]);
        Assert.False(warnings[1].Values.ContainsKey("UserId")); // an unknown account has no id to name
        Assert.Equal((bob.Id, "account disabled"), (warnings[2].Values["UserId"], warnings[2].Values["Reason"]));
        Assert.Equal((fresh.Id, "no password"), (warnings[3].Values["UserId"], warnings[3].Values["Reason"]));
        foreach (var typed in new[] { "alice@x.co", "ghost@x.co", "bob@x.co", "fresh@x.co", "wrong-password", "password-123456", "anything" })
            Assert.False(w.Log.Mentions(typed), typed);
    }

    [Fact]
    public async Task TheAttemptThatLocksAnAccount_SaysSo_AndSoDoesTheOneAfterIt()
    {
        var w = new World();
        w.AddUser("alice@x.co");
        var attempts = w.Options.Standalone.MaxFailedAttempts;

        for (var i = 0; i < attempts; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "wrong", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "password-123456", default)); // even the right password

        var lines = w.Log.From<AuthService>().Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Equal(attempts + 1, lines.Count);
        Assert.All(lines[..(attempts - 1)], e => Assert.Equal(false, e.Values["LockedOut"]));
        Assert.Equal(true, lines[attempts - 1].Values["LockedOut"]); // the attempt that locked it
        Assert.Equal("locked out", lines[attempts].Values["Reason"]); // and the one that found it locked
    }

    [Fact]
    public async Task ASignInAndAPasswordChange_AreInformation_NamingTheUser()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");

        await w.Auth.LoginAsync("alice@x.co", "password-123456", default);
        w.Current.SignInAs(alice);
        await w.Auth.ChangePasswordAsync("password-123456", "new-password-1234", default);

        var lines = w.Log.From<AuthService>().Where(e => e.Level >= LogLevel.Information).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(alice.Id, l.Values["UserId"]));
    }

    [Fact]
    public async Task AWrongCurrentPassword_WhenChangingIt_IsAWarning_AndNeitherPasswordIsLogged()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ChangePasswordAsync("not-the-password", "new-password-1234", default));

        var warning = Assert.Single(w.Log.From<AuthService>(), e => e.Level == LogLevel.Warning);
        Assert.Equal(alice.Id, warning.Values["UserId"]);
        Assert.False(w.Log.Mentions("not-the-password"));
        Assert.False(w.Log.Mentions("new-password-1234"));
    }

    [Fact]
    public async Task ADisabledUser_IsAWarning_FromAnIdentityProvider_ButOnlyDebug_FromAProxy_WhichSignsInOnEveryRequest()
    {
        var w = new World();
        var oidc = User.CreateExternal(UserProvider.Oidc, "sub-1", "o@x.co", "O", false);
        var proxy = User.CreateExternal(UserProvider.Proxy, "someone", "p@x.co", "P", false);
        oidc.SetDisabled(true);
        proxy.SetDisabled(true);
        w.Users.Items.AddRange([oidc, proxy]);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "sub-1", "o@x.co", "O", default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.ProvisionExternalAsync(UserProvider.Proxy, "someone", "p@x.co", "P", default));

        var lines = w.Log.From<AuthService>().ToList();
        Assert.Equal([LogLevel.Warning, LogLevel.Debug], lines.Select(e => e.Level));
        Assert.Equal([oidc.Id, proxy.Id], lines.Select(e => e.Values["UserId"]));
        foreach (var subject in new[] { "sub-1", "someone", "o@x.co", "p@x.co" })
            Assert.False(w.Log.Mentions(subject), subject); // the subject an identity provider or proxy sends can be the e-mail address
    }

    [Fact]
    public async Task ARefusedResetLink_SaysWhyInTheLog_ButNeverShowsTheLink()
    {
        var w = new World();
        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);
        var alice = w.AddUser("alice@x.co");
        var token = (await w.UserService.IssueResetAsync(alice.Id, default)).Token;

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync("garbage-link", "brand-new-password", default));
        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(token.Split('.')[0] + ".not-the-secret", "brand-new-password", default));

        var refused = w.Log.From<PasswordResetService>().ToList();
        Assert.Equal(["malformed link", "wrong secret"], refused.Select(e => e.Values["Reason"]));
        Assert.Equal("unknown", refused[0].Values["UserId"]);
        Assert.Equal(alice.Id.ToString(), refused[1].Values["UserId"]);
        foreach (var secret in new[] { token, token.Split('.')[1], "garbage-link", "not-the-secret" })
            Assert.False(w.Log.Mentions(secret), secret);
    }

    [Fact]
    public async Task UserAdministration_NamesTheAdministratorAndTheUserById()
    {
        var w = new World(smtp: true);
        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);

        var (created, _) = await w.UserService.CreateLocalAsync("new.person@x.co", "New Person", false, default);
        await w.UserService.SetAdminAsync(created.Id, true, default);
        await w.UserService.SetDisabledAsync(created.Id, true, default);
        await w.UserService.DeleteAsync(created.Id, null, null, default);

        var lines = w.Log.From<UserService>().ToList();
        Assert.Equal(5, lines.Count); // created, issued the setup link, made administrator, disabled, deleted
        Assert.All(lines, l =>
        {
            Assert.Equal(LogLevel.Information, l.Level);
            Assert.Equal(admin.Id, l.Values["AdminId"]);
            Assert.Equal(created.Id, l.Values["UserId"]);
        });
        Assert.Equal(true, lines[1].Values["EmailSent"]);
        Assert.Equal("none", lines[4].Values["Data"]); // the user owned nothing
        foreach (var typed in new[] { "new.person@x.co", "New Person" })
            Assert.False(w.Log.Mentions(typed), typed);
    }

    [Fact]
    public async Task AccessChanges_AreLoggedWhenTheyHappen_NotWhenNothingChanged()
    {
        var w = new World();
        var admin = w.AddUser("root@x.co", admin: true);
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(admin);

        await w.AccessAdmin.SetDefaultLevelAsync(AccessLevel.View, default);
        await w.AccessAdmin.SetDefaultLevelAsync(AccessLevel.View, default); // the same again
        await w.AccessAdmin.SetGrantAsync(alice.Id, bob.Id, AccessLevel.Edit, default);
        await w.AccessAdmin.SetGrantAsync(alice.Id, bob.Id, AccessLevel.Edit, default);
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, default);
        await w.Sharing.SetLogAccessAsync(car.Id, bob.Id, AccessLevel.Edit, default);
        await w.Sharing.SetLogAccessAsync(car.Id, bob.Id, AccessLevel.Edit, default);

        var byAdmin = w.Log.From<AccessAdminService>().ToList();
        Assert.Equal(2, byAdmin.Count); // the default level once, the grant once
        Assert.All(byAdmin, l => Assert.Equal(admin.Id, l.Values["AdminId"]));
        Assert.Equal((AccessLevel.None, AccessLevel.View), ((AccessLevel)byAdmin[0].Values["Before"]!, (AccessLevel)byAdmin[0].Values["After"]!));
        Assert.Equal((alice.Id, bob.Id), (byAdmin[1].Values["OwnerId"], byAdmin[1].Values["GranteeId"]));
        var sharing = Assert.Single(w.Log.From<ResourceSharingService>());
        Assert.Equal([alice.Id, bob.Id, car.Id], [sharing.Values["ActorId"], sharing.Values["GranteeId"], sharing.Values["VehicleId"]]);
        Assert.All(byAdmin.Append(sharing), l => Assert.Equal(LogLevel.Information, l.Level));
    }
}
