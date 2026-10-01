using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Users;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Infrastructure.Auth;

namespace Tankstat.Infrastructure.UnitTests;

public class AuthRepositoryTests
{
    [Fact]
    public async Task Users_RoundTrip_AndLookups()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUserRepository>();
        var local = User.CreateLocal("Alice@x.co", "Alice", true);
        local.SetPasswordHash("hash");
        var oidc = User.CreateExternal(UserProvider.Oidc, "sub-1", "a@x.co", "A", false);
        await repo.AddAsync(local, default);
        await repo.AddAsync(oidc, default);

        var found = await repo.FindLocalByEmailAsync("alice@x.co", default);

        Assert.Equal(local.Id, found!.Id);
        Assert.Equal("hash", found.PasswordHash);
        Assert.True(found.IsAdmin);
        Assert.Null(await repo.FindLocalByEmailAsync("sub-1", default));          // external users are not local logins
        Assert.Equal(oidc.Id, (await repo.FindExternalAsync(UserProvider.Oidc, "sub-1", default))!.Id);
        Assert.Null(await repo.FindExternalAsync(UserProvider.Proxy, "sub-1", default));
        Assert.True(await repo.AnyLocalAdminAsync(default));
        Assert.Equal(2, (await repo.ListAsync(default)).Count);
    }

    [Fact]
    public async Task Users_UpdatePersistsCredentialsAndFlags()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUserRepository>();
        var user = User.CreateLocal("a@x.co", null, false);
        await repo.AddAsync(user, default);

        var loaded = (await repo.FindByIdAsync(user.Id, default))!;
        loaded.SetPasswordHash("new");
        loaded.SetDisabled(true);
        loaded.RegisterFailedLogin(DateTimeOffset.UtcNow, 1, TimeSpan.FromMinutes(5));
        await repo.UpdateAsync(loaded, default);

        var again = (await repo.FindByIdAsync(user.Id, default))!;
        Assert.Equal("new", again.PasswordHash);
        Assert.True(again.IsDisabled);
        Assert.Equal(loaded.SessionVersion, again.SessionVersion);
        Assert.NotNull(again.LockoutEnd);
        Assert.False(await repo.AnyLocalAdminAsync(default));
    }

    [Fact]
    public async Task Users_AreUniquePerProviderAndSubject()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUserRepository>();
        await repo.AddAsync(User.CreateLocal("a@x.co", null, false), default);

        await Assert.ThrowsAnyAsync<Exception>(() => repo.AddAsync(User.CreateLocal("a@x.co", null, false), default));
        await repo.AddAsync(User.CreateExternal(UserProvider.Oidc, "a@x.co", null, null, false), default); // other provider is fine
    }

    [Fact]
    public async Task ResetTokens_RoundTrip()
    {
        await using var db = new TestDatabase();
        var user = User.CreateLocal("a@x.co", null, false);
        await db.Get<IUserRepository>().AddAsync(user, default);
        var repo = db.Get<IPasswordResetTokenRepository>();
        var now = DateTimeOffset.UtcNow;
        var token = PasswordResetToken.Issue(user.Id, "hash", now, TimeSpan.FromHours(1));
        await repo.AddAsync(token, default);

        var loaded = (await repo.FindAsync(token.Id, default))!;
        loaded.MarkUsed(now);
        await repo.UpdateAsync(loaded, default);

        Assert.False((await repo.FindAsync(token.Id, default))!.IsUsable(now));
        Assert.Null(await repo.FindAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Grants_RoundTrip_AreUniquePerPair_AndRemovable()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var owner = User.CreateLocal("o@x.co", null, false);
        var grantee = User.CreateLocal("g@x.co", null, false);
        await users.AddAsync(owner, default);
        await users.AddAsync(grantee, default);
        var repo = db.Get<IAccessGrantRepository>();
        var grant = AccessGrant.Create(owner.Id, grantee.Id, AccessLevel.View);
        await repo.AddAsync(grant, default);

        var loaded = (await repo.FindAsync(owner.Id, grantee.Id, default))!;
        loaded.ChangeLevel(AccessLevel.Edit);
        await repo.UpdateAsync(loaded, default);

        Assert.Equal(AccessLevel.Edit, Assert.Single(await repo.ListForGranteeAsync(grantee.Id, default)).Level);
        Assert.Empty(await repo.ListForGranteeAsync(owner.Id, default));
        await Assert.ThrowsAnyAsync<Exception>(() => repo.AddAsync(AccessGrant.Create(owner.Id, grantee.Id, AccessLevel.View), default));

        await repo.RemoveAsync(loaded, default);
        Assert.Empty(await repo.ListAsync(default));
    }

    [Fact]
    public async Task Settings_DefaultToNone_AndPersistChanges()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IAccessSettingsRepository>();

        var settings = await repo.GetAsync(default);
        Assert.Equal(AccessLevel.None, settings.DefaultLevelForOthers);

        settings.SetDefaultLevelForOthers(AccessLevel.View);
        await repo.SaveAsync(settings, default);
        Assert.Equal(AccessLevel.View, (await repo.GetAsync(default)).DefaultLevelForOthers);

        settings.SetDefaultLevelForOthers(AccessLevel.Edit); // second save updates the same row
        await repo.SaveAsync(settings, default);
        Assert.Equal(AccessLevel.Edit, (await repo.GetAsync(default)).DefaultLevelForOthers);
    }
}

public class PasswordHasherTests
{
    [Fact]
    public void Hash_VerifiesOnlyTheRightPassword_AndIsSalted()
    {
        IPasswordHasher hasher = new IdentityPasswordHasher();

        var hash = hasher.Hash("correct horse battery");

        Assert.True(hasher.Verify(hash, "correct horse battery"));
        Assert.False(hasher.Verify(hash, "Correct horse battery"));
        Assert.NotEqual(hash, hasher.Hash("correct horse battery"));
        Assert.DoesNotContain("correct", hash);
    }
}
