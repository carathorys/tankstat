using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// What an operator learns about people from the log: who failed to sign in (by account id, never by the address they typed), who changed
/// a password, what an administrator did to whom, and who changed whose access. Nothing a person typed and no secret is ever in a line.
/// </summary>
public class AccountLoggingTests
{
    private static IEnumerable<LogEntry> From<T>(World w) => w.Log.Entries.Where(e => e.Category == typeof(T).FullName);

    [Fact]
    public async Task FailedSignIns_AreWarnings_ThatNameTheAccountByIdAndNeverByAddress()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        bob.SetDisabled(true);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "wrong-password", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("ghost@x.co", "wrong-password", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("bob@x.co", "password-123456", default));

        var warnings = w.Log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, m => m.Contains(alice.Id.ToString()) && m.Contains("wrong password"));
        Assert.Contains(warnings, m => m.Contains("unknown account"));
        Assert.Contains(warnings, m => m.Contains(bob.Id.ToString()) && m.Contains("disabled"));
        foreach (var typed in new[] { "alice@x.co", "ghost@x.co", "bob@x.co", "wrong-password", "password-123456" })
            Assert.False(w.Log.Mentions(typed), typed);
    }

    [Fact]
    public async Task TheAttemptThatLocksAnAccount_SaysSo()
    {
        var w = new World();
        w.AddUser("alice@x.co");

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "wrong", default));

        var lines = w.Log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(5, lines.Count);
        Assert.All(lines[..4], m => Assert.Contains("locked out: False", m));
        Assert.Contains("locked out: True", lines[4]);
    }

    [Fact]
    public async Task ASignInAndAPasswordChange_AreInformation_NamingTheUser()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");

        await w.Auth.LoginAsync("alice@x.co", "password-123456", default);
        w.Current.SignInAs(alice);
        await w.Auth.ChangePasswordAsync("password-123456", "new-password-1234", default);

        var lines = w.Log.Entries.Where(e => e.Level >= LogLevel.Information).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Contains(alice.Id.ToString(), l.Message));
        Assert.Contains("signed in", lines[0].Message);
        Assert.Contains("changed their password", lines[1].Message);
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

        var refused = w.Log.Entries.Where(e => e.Message.StartsWith("Password reset link refused", StringComparison.Ordinal)).Select(e => e.Message).ToList();
        Assert.Contains(refused, m => m.Contains("malformed"));
        Assert.Contains(refused, m => m.Contains("wrong secret") && m.Contains(alice.Id.ToString()));
        foreach (var secret in new[] { token, token.Split('.')[1], "garbage-link", "not-the-secret" })
            Assert.False(w.Log.Mentions(secret), secret);
    }

    // ---- what an administrator does ---------------------------------------------------------------------------

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

        var lines = From<UserService>(w).ToList();
        Assert.Equal(5, lines.Count); // created, issued the setup link, made administrator, disabled, deleted
        Assert.All(lines, l =>
        {
            Assert.Equal(LogLevel.Information, l.Level);
            Assert.Contains(admin.Id.ToString(), l.Message);
            Assert.Contains(created.Id.ToString(), l.Message);
        });
        Assert.Contains("e-mail sent: True", lines[1].Message);
        Assert.Contains("data: none", lines[4].Message);
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

        var byAdmin = From<Application.Access.AccessAdminService>(w).ToList();
        Assert.Equal(2, byAdmin.Count);
        Assert.All(byAdmin, l => Assert.Contains(admin.Id.ToString(), l.Message));
        var sharing = Assert.Single(From<Application.Sharing.ResourceSharingService>(w));
        Assert.All(new[] { alice.Id, bob.Id, car.Id }, id => Assert.Contains(id.ToString(), sharing.Message));
        Assert.All(byAdmin.Append(sharing), l => Assert.Equal(LogLevel.Information, l.Level));
    }
}
