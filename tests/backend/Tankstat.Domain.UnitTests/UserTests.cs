using Tankstat.Domain;
using Tankstat.Domain.Users;

namespace Tankstat.Domain.UnitTests;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateLocal_NormalizesEmail_AndUsesItAsSubject()
    {
        var u = User.CreateLocal("  Alice@Example.COM ", null, true);

        Assert.Equal("alice@example.com", u.Email);
        Assert.Equal("alice@example.com", u.Subject);
        Assert.Equal("alice@example.com", u.DisplayName);
        Assert.Equal(UserProvider.Local, u.Provider);
        Assert.True(u.IsAdmin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Name <a@b.c>")]
    public void CreateLocal_RejectsInvalidEmail(string email) =>
        Assert.Throws<DomainException>(() => User.CreateLocal(email, null, false));

    [Fact]
    public void CreateExternal_RequiresSubject_AndRejectsLocal()
    {
        Assert.Throws<DomainException>(() => User.CreateExternal(UserProvider.Oidc, " ", null, null, false));
        Assert.Throws<DomainException>(() => User.CreateExternal(UserProvider.Local, "x", null, null, false));
        Assert.Equal("sub-1", User.CreateExternal(UserProvider.Proxy, "sub-1", null, null, false).DisplayName);
    }

    [Fact]
    public void FailedLogins_LockTheAccountAndResetTheCounter()
    {
        var u = User.CreateLocal("a@b.co", null, false);

        for (var i = 0; i < 4; i++) u.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        Assert.False(u.IsLockedOut(Now));

        u.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        Assert.True(u.IsLockedOut(Now));
        Assert.True(u.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(u.IsLockedOut(Now.AddMinutes(16)));
    }

    [Fact]
    public void SetPasswordHash_ClearsLockout_AndInvalidatesSessions()
    {
        var u = User.CreateLocal("a@b.co", null, false);
        for (var i = 0; i < 5; i++) u.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        var version = u.SessionVersion;

        u.SetPasswordHash("hash");

        Assert.False(u.IsLockedOut(Now));
        Assert.Equal("hash", u.PasswordHash);
        Assert.Equal(version + 1, u.SessionVersion);
    }

    [Fact]
    public void ExternalUsers_HaveNoPassword() =>
        Assert.Throws<DomainException>(() => User.CreateExternal(UserProvider.Oidc, "s", null, null, false).SetPasswordHash("x"));

    [Fact]
    public void ExternalUsers_ProfileComesFromTheirProvider_NotFromAnAdministrator()
    {
        var u = User.CreateExternal(UserProvider.Oidc, "s", "a@b.co", "Ann", false);

        Assert.Equal("user.profileLocalOnly", Assert.Throws<DomainException>(() => u.ChangeLocalProfile("other@b.co", "Other")).Key);
        Assert.Equal(("a@b.co", "Ann"), (u.Email, u.DisplayName));

        var local = User.CreateLocal("a@b.co", "Ann", false);
        local.ChangeLocalProfile(" Other@B.co ", "Other");
        Assert.Equal(("other@b.co", "other@b.co", "Other"), (local.Email, local.Subject, local.DisplayName)); // the sign-in identity follows the e-mail
    }

    [Fact]
    public void Disabling_InvalidatesSessions()
    {
        var u = User.CreateLocal("a@b.co", null, false);
        var version = u.SessionVersion;

        u.SetDisabled(true);

        Assert.True(u.IsDisabled);
        Assert.Equal(version + 1, u.SessionVersion);
    }
}

public class PasswordTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public void Policy_RejectsShortPasswords(string? password) =>
        Assert.Throws<DomainException>(() => PasswordPolicy.Validate(password));

    [Fact]
    public void Policy_RejectsHugePasswords() =>
        Assert.Throws<DomainException>(() => PasswordPolicy.Validate(new string('x', PasswordPolicy.MaxLength + 1)));

    [Fact]
    public void Policy_AcceptsReasonablePasswords() => PasswordPolicy.Validate("correct horse battery");

    [Fact]
    public void ResetToken_IsSingleUse_AndExpires()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var token = PasswordResetToken.Issue(Guid.NewGuid(), "hash", now, TimeSpan.FromHours(1));

        Assert.True(token.IsUsable(now));
        Assert.False(token.IsUsable(now.AddHours(2)));

        token.MarkUsed(now);
        Assert.False(token.IsUsable(now));
    }
}
