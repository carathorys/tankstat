using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Users;
using Tankstat.TestSupport;

namespace Tankstat.Application.UnitTests;

public class LoginTests
{
    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsUser()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");

        var user = await w.Auth.LoginAsync(" Alice@X.co ", "password-123456", default);

        Assert.Same(alice, user);
    }

    [Theory]
    [InlineData("alice@x.co", "wrong-password")]
    [InlineData("nobody@x.co", "password-123456")]
    [InlineData("not-an-email", "password-123456")]
    [InlineData(null, null)]
    public async Task Login_Failure_IsGeneric(string? email, string? password)
    {
        var w = new World();
        w.AddUser("alice@x.co");

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync(email, password, default));
    }

    [Fact]
    public async Task Login_LocksAccountAfterRepeatedFailures_EvenForTheRightPassword_UntilItExpires()
    {
        var w = new World();
        w.AddUser("alice@x.co");

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "wrong", default));
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "password-123456", default));

        w.Clock.Advance(TimeSpan.FromMinutes(16));
        await w.Auth.LoginAsync("alice@x.co", "password-123456", default);
    }

    [Fact]
    public async Task Login_DisabledUser_IsRejected()
    {
        var w = new World();
        w.AddUser("alice@x.co").SetDisabled(true);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "password-123456", default));
    }

    [Fact]
    public async Task Login_UserWithoutPassword_IsRejected()
    {
        var w = new World();
        w.Users.Items.Add(User.CreateLocal("new@x.co", null, false));

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("new@x.co", "", default));
    }

    [Theory]
    [InlineData(AuthMode.None)]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.ProxyHeader)]
    public async Task Login_IsOnlyAvailableInStandaloneMode(AuthMode mode)
    {
        var w = new World(mode);
        w.AddUser("alice@x.co");

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.LoginAsync("alice@x.co", "password-123456", default));
    }
}

public class PasswordTests
{
    [Fact]
    public async Task ChangePassword_RequiresCurrentPassword_AndInvalidatesSessions()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);
        var version = alice.SessionVersion;

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ChangePasswordAsync("wrong", "new-password-123", default));
        await w.Auth.ChangePasswordAsync("password-123456", "new-password-123", default);

        Assert.Equal(version + 1, alice.SessionVersion);
        await w.Auth.LoginAsync("alice@x.co", "new-password-123", default);
    }

    [Fact]
    public async Task ChangePassword_EnforcesPolicy()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ChangePasswordAsync("password-123456", "short", default));
    }

    [Fact]
    public async Task ChangePassword_NeedsSignIn()
    {
        var w = new World();
        w.AddUser("alice@x.co");

        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.Auth.ChangePasswordAsync("password-123456", "new-password-123", default));
    }

    [Fact]
    public async Task RequestReset_ForUnknownEmail_DoesNothingAndDoesNotFail()
    {
        var w = new World(smtp: true);

        await w.Auth.RequestPasswordResetAsync("ghost@x.co", default);
        await w.Auth.RequestPasswordResetAsync("garbage", default);

        Assert.Empty(w.Email.Sent);
        Assert.Empty(w.Tokens.Items);
    }

    [Fact]
    public async Task RequestReset_WithSmtp_EmailsALinkThatResetsThePassword()
    {
        var w = new World(smtp: true);
        var alice = w.AddUser("alice@x.co");

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);

        var mail = Assert.Single(w.Email.Sent);
        Assert.Equal("alice@x.co", mail.To);
        var token = mail.Body.Split("resetToken=")[1].Split('\n')[0];
        Assert.StartsWith("https://tank.test/?resetToken=", mail.Body[mail.Body.IndexOf("https", StringComparison.Ordinal)..]);

        await w.Auth.ResetPasswordAsync(token, "brand-new-password", default);
        await w.Auth.LoginAsync("alice@x.co", "brand-new-password", default);
        Assert.True(alice.SessionVersion > 1);
    }

    [Fact]
    public async Task RequestReset_WithoutSmtp_SendsNothing()
    {
        var w = new World(smtp: false);
        w.AddUser("alice@x.co");

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);

        Assert.Empty(w.Email.Sent);
        Assert.Empty(w.Tokens.Items); // a link nobody receives is not issued
    }

    [Fact]
    public async Task RequestReset_WithoutSmtp_LeavesAnAdministratorsLinkAlone()
    {
        var w = new World(smtp: false);
        var token = await IssueToken(w, "alice@x.co");
        w.Clock.Advance(TimeSpan.FromMinutes(10)); // past the cool-down

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);

        await w.Auth.ResetPasswordAsync(token, "brand-new-password", default);
    }

    private static string LinkIn((string To, string Subject, string Body) mail) => mail.Body.Split("resetToken=")[1].Split('\n')[0];

    /// <summary>The lines about <paramref name="user"/>'s own requests that issued nothing (they carry no <c>EmailSent</c>).</summary>
    private static List<LogEntry> Withheld(World w, User user) => w.Log.From<AuthService>()
        .Where(e => Equals(e.Values.GetValueOrDefault("UserId"), user.Id) && !e.Values.ContainsKey("EmailSent")).ToList();

    [Fact]
    public async Task RequestReset_WithinCooldown_SendsNothingAndIssuesNothing()
    {
        var w = new World(smtp: true);
        var alice = w.AddUser("alice@x.co");

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);
        w.Clock.Advance(TimeSpan.FromMinutes(4));
        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);
        await w.Auth.RequestPasswordResetAsync("ALICE@x.co", default);

        Assert.Single(w.Email.Sent);
        Assert.Single(w.Tokens.Items);
        var lines = Withheld(w, alice);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(LogLevel.Information, l.Level));
        await w.Auth.ResetPasswordAsync(LinkIn(w.Email.Sent[0]), "brand-new-password", default); // the first link still works
    }

    [Fact]
    public async Task RequestReset_AfterCooldown_ReplacesTheOlderLink()
    {
        var w = new World(smtp: true);
        w.AddUser("alice@x.co");

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);
        w.Clock.Advance(TimeSpan.FromMinutes(5));
        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);

        Assert.Equal(2, w.Email.Sent.Count);
        Assert.Single(w.Tokens.Items);
        var refused = await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(LinkIn(w.Email.Sent[0]), "brand-new-password", default));
        Assert.Equal("reset.invalid", refused.Key);
        await w.Auth.ResetPasswordAsync(LinkIn(w.Email.Sent[1]), "brand-new-password", default);
    }

    [Fact]
    public async Task AdminIssuedReset_IsNeverThrottled_AndReplacesOlderLinks()
    {
        var w = new World(smtp: true);
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);
        var first = await w.UserService.IssueResetAsync(alice.Id, default);
        var second = await w.UserService.IssueResetAsync(alice.Id, default);

        Assert.Equal(3, w.Email.Sent.Count);
        Assert.Single(w.Tokens.Items);
        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(LinkIn(w.Email.Sent[0]), "brand-new-password", default));
        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(first.Token, "brand-new-password", default));
        await w.Auth.ResetPasswordAsync(second.Token, "brand-new-password", default);
    }

    [Fact]
    public async Task ExpiredTokens_ArePurgedOnTheNextIssue()
    {
        var w = new World(smtp: true);
        var bob = w.AddUser("bob@x.co");
        var carol = w.AddUser("carol@x.co");
        w.AddUser("alice@x.co");

        await w.Auth.RequestPasswordResetAsync("bob@x.co", default);
        w.Clock.Advance(TimeSpan.FromHours(23)); // carol's link expires, but less than a day ago by the end
        await w.Auth.RequestPasswordResetAsync("carol@x.co", default);
        w.Clock.Advance(TimeSpan.FromHours(3)); // bob's expired more than a day ago
        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);

        Assert.DoesNotContain(w.Tokens.Items, t => t.UserId == bob.Id);
        Assert.Contains(w.Tokens.Items, t => t.UserId == carol.Id);
        Assert.Equal(2, w.Tokens.Items.Count);
    }

    [Fact]
    public async Task ResetCooldownZero_TurnsItOff()
    {
        var w = new World(smtp: true, configure: o => o.Standalone.ResetCooldownMinutes = 0);
        w.AddUser("alice@x.co");

        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);
        await w.Auth.RequestPasswordResetAsync("alice@x.co", default);

        Assert.Equal(2, w.Email.Sent.Count);
        Assert.Single(w.Tokens.Items); // still one live link
    }

    private static async Task<string> IssueToken(World w, string email)
    {
        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);
        var user = w.AddUser(email);
        return (await w.UserService.IssueResetAsync(user.Id, default)).Token;
    }

    [Fact]
    public async Task ResetToken_IsSingleUse()
    {
        var w = new World();
        var token = await IssueToken(w, "alice@x.co");

        await w.Auth.ResetPasswordAsync(token, "brand-new-password", default);

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(token, "another-password-1", default));
    }

    [Fact]
    public async Task ResetToken_Expires()
    {
        var w = new World();
        var token = await IssueToken(w, "alice@x.co");

        w.Clock.Advance(TimeSpan.FromMinutes(61));

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(token, "brand-new-password", default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("00000000000000000000000000000000.secret")]
    public async Task ResetToken_Garbage_IsRejected(string token)
    {
        var w = new World();

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(token, "brand-new-password", default));
    }

    [Fact]
    public async Task ResetToken_WithWrongSecret_IsRejected()
    {
        var w = new World();
        var token = await IssueToken(w, "alice@x.co");

        await Assert.ThrowsAsync<DomainException>(() =>
            w.Auth.ResetPasswordAsync(token.Split('.')[0] + ".not-the-secret", "brand-new-password", default));
    }

    [Fact]
    public async Task ResetPassword_EnforcesPolicy_WithoutConsumingTheToken()
    {
        var w = new World();
        var token = await IssueToken(w, "alice@x.co");

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync(token, "short", default));
        await w.Auth.ResetPasswordAsync(token, "long-enough-password", default);
    }
}

public class ProvisioningTests
{
    [Fact]
    public async Task FirstExternalLogin_CreatesTheUser_AndLaterLoginsReuseIt()
    {
        var w = new World(AuthMode.Oidc);

        var first = await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "sub-1", "a@x.co", "Alice", default);
        var second = await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "sub-1", "new@x.co", "Alice B", default);

        Assert.Same(first, second);
        Assert.Equal("new@x.co", second.Email);
        Assert.Equal("Alice B", second.DisplayName);
        Assert.False(second.IsAdmin);
        Assert.Single(w.Users.Items);
    }

    [Fact]
    public async Task ConfiguredAdminEmails_BecomeAdministrators_ByEmailOrSubject()
    {
        var w = new World(AuthMode.Oidc, configure: o => o.AdminEmails = ["Boss@X.co", "root-user"]);

        var byEmail = await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "s1", "boss@x.co", null, default);
        var bySubject = await w.Auth.ProvisionExternalAsync(UserProvider.Proxy, "root-user", null, null, default);
        var other = await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "s2", "other@x.co", null, default);

        Assert.True(byEmail.IsAdmin);
        Assert.True(bySubject.IsAdmin);
        Assert.False(other.IsAdmin);
    }

    [Fact]
    public async Task ExistingUser_IsPromotedWhenAddedToAdminList()
    {
        var w = new World(AuthMode.Oidc);
        await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "s1", "boss@x.co", null, default);
        w.Options.AdminEmails = ["boss@x.co"];

        Assert.True((await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "s1", "boss@x.co", null, default)).IsAdmin);
    }

    [Fact]
    public async Task DisabledUser_CannotSignIn()
    {
        var w = new World(AuthMode.Oidc);
        var user = await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "s1", "a@x.co", null, default);
        user.SetDisabled(true);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "s1", "a@x.co", null, default));
    }

    [Fact]
    public async Task SameSubject_InDifferentProviders_AreDifferentUsers()
    {
        var w = new World(AuthMode.Oidc);

        var oidc = await w.Auth.ProvisionExternalAsync(UserProvider.Oidc, "same", null, null, default);
        var proxy = await w.Auth.ProvisionExternalAsync(UserProvider.Proxy, "same", null, null, default);

        Assert.NotEqual(oidc.Id, proxy.Id);
    }
}

public class BootstrapTests
{
    [Fact]
    public async Task CreatesTheFirstAdministrator_FromConfiguration()
    {
        var w = new World(configure: o => { o.Standalone.AdminEmail = "root@x.co"; o.Standalone.AdminPassword = "initial-password-1"; });

        await w.Auth.BootstrapAdminAsync(default);

        var admin = Assert.Single(w.Users.Items);
        Assert.True(admin.IsAdmin);
        await w.Auth.LoginAsync("root@x.co", "initial-password-1", default);
    }

    [Fact]
    public async Task DoesNothing_WhenAnAdministratorAlreadyExists()
    {
        var w = new World(configure: o => { o.Standalone.AdminEmail = "other@x.co"; o.Standalone.AdminPassword = "initial-password-1"; });
        w.AddUser("root@x.co", admin: true);

        await w.Auth.BootstrapAdminAsync(default);

        Assert.Single(w.Users.Items);
    }

    [Fact]
    public async Task Fails_WhenThereIsNoAdministratorAndNoConfiguration()
    {
        var w = new World();

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Auth.BootstrapAdminAsync(default));
    }

    [Fact]
    public async Task Fails_WhenTheConfiguredPasswordIsTooWeak()
    {
        var w = new World(configure: o => { o.Standalone.AdminEmail = "root@x.co"; o.Standalone.AdminPassword = "weak"; });

        await Assert.ThrowsAsync<DomainException>(() => w.Auth.BootstrapAdminAsync(default));
    }

    [Theory]
    [InlineData(AuthMode.None)]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.ProxyHeader)]
    public async Task IsSkipped_OutsideStandaloneMode(AuthMode mode)
    {
        await new World(mode).Auth.BootstrapAdminAsync(default);
    }
}

public class UserAdminTests
{
    [Fact]
    public async Task OnlyAdministrators_ManageUsers()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.UserService.ListAsync(default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.UserService.CreateLocalAsync("n@x.co", null, false, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.UserService.SetAdminAsync(alice.Id, true, default));
    }

    [Fact]
    public async Task CreateUser_IssuesASetupLink_AndEmailsItWhenSmtpIsConfigured()
    {
        var w = new World(smtp: true);
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));

        var (user, reset) = await w.UserService.CreateLocalAsync("New@X.co", "Newbie", false, default);

        Assert.Equal("new@x.co", user.Email);
        Assert.Null(user.PasswordHash);
        Assert.True(reset.EmailSent);
        Assert.Contains("resetToken=", reset.Url);
        Assert.Single(w.Email.Sent);

        await w.Auth.ResetPasswordAsync(reset.Token, "chosen-password-1", default);
        await w.Auth.LoginAsync("new@x.co", "chosen-password-1", default);
    }

    [Fact]
    public async Task CreateUser_WithoutSmtp_ReturnsTheLinkForTheAdministratorToHandOver()
    {
        var w = new World(smtp: false);
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));

        var (_, reset) = await w.UserService.CreateLocalAsync("new@x.co", null, false, default);

        Assert.False(reset.EmailSent);
        Assert.NotEmpty(reset.Token);
        Assert.Empty(w.Email.Sent);
    }

    [Fact]
    public async Task CreateUser_RejectsDuplicates_AndOtherModes()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));
        w.AddUser("taken@x.co");

        await Assert.ThrowsAsync<DomainException>(() => w.UserService.CreateLocalAsync("TAKEN@x.co", null, false, default));

        w.Options.Mode = AuthMode.Oidc;
        await Assert.ThrowsAsync<DomainException>(() => w.UserService.CreateLocalAsync("new@x.co", null, false, default));
    }

    [Fact]
    public async Task Administrators_CannotLockThemselvesOut()
    {
        var w = new World();
        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);

        await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetAdminAsync(admin.Id, false, default));
        await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetDisabledAsync(admin.Id, true, default));
    }

    [Fact]
    public async Task Administrator_CanPromoteAndDisableOthers()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));
        var alice = w.AddUser("alice@x.co");

        await w.UserService.SetAdminAsync(alice.Id, true, default);
        await w.UserService.SetDisabledAsync(alice.Id, true, default);

        Assert.True(alice.IsAdmin);
        Assert.True(alice.IsDisabled);
    }
}

public class NoticeAndOptionsTests
{
    [Fact]
    public void NoAuth_ProducesTheWarning_OtherModesDoNot()
    {
        var warning = Assert.Single(new NoticeService(new AuthOptions { Mode = AuthMode.None }.Create()).GetNotices());
        Assert.Equal("AUTH_DISABLED", warning.Code);
        Assert.Equal(NoticeSeverity.Warning, warning.Severity);

        foreach (var mode in new[] { AuthMode.Standalone, AuthMode.Oidc, AuthMode.ProxyHeader })
            Assert.Empty(new NoticeService(new AuthOptions { Mode = mode }.Create()).GetNotices());
    }

    private static ValidateOptionsResult Validate(AuthOptions o, SmtpOptions? smtp = null) =>
        new AuthOptionsValidator((smtp ?? new SmtpOptions()).Create()).Validate(null, o);

    [Fact]
    public void Validation_RequiresOidcSettings()
    {
        Assert.True(Validate(new AuthOptions { Mode = AuthMode.Oidc }).Failed);
        Assert.True(Validate(new AuthOptions
        {
            Mode = AuthMode.Oidc,
            Oidc = { Authority = "https://id.test", ClientId = "c", ClientSecret = "s" },
        }).Succeeded);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1440, true)]
    [InlineData(1441, false)]
    public void Validation_LimitsTheResetCooldown(int minutes, bool valid) =>
        Assert.Equal(valid, Validate(new AuthOptions { Standalone = { ResetCooldownMinutes = minutes } }).Succeeded);

    [Fact]
    public void Validation_RequiresTrustedProxies_ForProxyMode()
    {
        Assert.True(Validate(new AuthOptions { Mode = AuthMode.ProxyHeader }).Failed);
        Assert.True(Validate(new AuthOptions { Mode = AuthMode.ProxyHeader, ProxyHeader = { TrustedProxies = ["10.0.0.0/8"] } }).Succeeded);
    }

    [Fact]
    public void Validation_RequiresPublicUrl_WhenSmtpIsConfigured()
    {
        var smtp = new SmtpOptions { Host = "mail", From = "tank@x.co" };

        Assert.True(Validate(new AuthOptions { Mode = AuthMode.Standalone }, smtp).Failed);
        Assert.True(Validate(new AuthOptions { Mode = AuthMode.Standalone, PublicUrl = "https://t.co" }, smtp).Succeeded);
    }

    [Theory]
    [InlineData((AuthMode)99, 15, 90, 60, "Auth:Mode")]
    [InlineData(AuthMode.Standalone, 0, 90, 60, "Auth:AccessTokenMinutes")]
    [InlineData(AuthMode.Standalone, 1441, 90, 60, "Auth:AccessTokenMinutes")]
    [InlineData(AuthMode.Standalone, 15, 0, 60, "Auth:RefreshTokenDays")]
    [InlineData(AuthMode.Standalone, 15, 3651, 60, "Auth:RefreshTokenDays")]
    [InlineData(AuthMode.Standalone, 15, 90, -1, "Auth:RefreshRotationGraceSeconds")]
    [InlineData(AuthMode.Standalone, 15, 90, 3601, "Auth:RefreshRotationGraceSeconds")]
    public void Validation_KeepsTheModeAndTheSessionLifetimesInRange(AuthMode mode, int accessMinutes, int refreshDays, int graceSeconds, string named)
    {
        var result = Validate(new AuthOptions { Mode = mode, AccessTokenMinutes = accessMinutes, RefreshTokenDays = refreshDays, RefreshRotationGraceSeconds = graceSeconds });

        Assert.StartsWith(named + " ", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validation_RequiresTheUserHeader_ForProxyMode()
    {
        var result = Validate(new AuthOptions { Mode = AuthMode.ProxyHeader, ProxyHeader = { UserHeader = " ", TrustedProxies = ["10.0.0.0/8"] } });

        Assert.Contains("Auth:ProxyHeader:UserHeader", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Validation_AcceptsNoAuthAndPlainStandalone()
    {
        Assert.True(Validate(new AuthOptions()).Succeeded);
        Assert.True(Validate(new AuthOptions { Mode = AuthMode.Standalone }).Succeeded);
    }
}
