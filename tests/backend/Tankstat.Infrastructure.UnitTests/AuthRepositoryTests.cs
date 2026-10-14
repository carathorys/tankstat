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
    public async Task Users_TheSameProviderAndSubjectTwice_IsRefusedWithFalse_AndTheFirstStays()
    {
        // Two requests that both found no such user both insert; the unique index (Provider, Subject) keeps the first.
        await using var db = new TestDatabase();
        var repo = db.Get<IUserRepository>();
        var first = User.CreateExternal(UserProvider.Proxy, "sub-1", "a@x.co", "A", false);
        var second = User.CreateExternal(UserProvider.Proxy, "sub-1", "a@x.co", "A", false);
        var otherProvider = User.CreateExternal(UserProvider.Oidc, "sub-1", "a@x.co", "A", false);

        Assert.True(await repo.AddAsync(first, default));
        Assert.False(await repo.AddAsync(second, default));
        Assert.True(await repo.AddAsync(otherProvider, default)); // the same subject at another provider is another user
        Assert.True(await repo.AddAsync(User.CreateLocal("a@x.co", "A", false), default));
        Assert.False(await repo.AddAsync(User.CreateLocal("A@X.co", "Again", true), default)); // the address is normalised: the same subject

        Assert.Equal(3, (await repo.ListAsync(default)).Count);
        Assert.Equal(first.Id, (await repo.FindExternalAsync(UserProvider.Proxy, "sub-1", default))!.Id);
        Assert.Null(await repo.FindByIdAsync(second.Id, default)); // the loser's own id was never stored
    }

    [Fact]
    public async Task Users_AddedAtOnce_OneIsSaved_TheOthersAreToldSo()
    {
        // What a second server on the same database does (the in-process lock of the services does not hold it back): all insert at once.
        await using var db = new TestDatabase();
        var repo = db.Get<IUserRepository>();

        var saved = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repo.AddAsync(User.CreateExternal(UserProvider.Proxy, "sub-1", null, null, false), default)));

        Assert.Single(saved, s => s);
        Assert.Single(await repo.ListAsync(default));
    }

    [Fact]
    public async Task Users_AnInsertThatFailsForAnotherReason_IsStillAnError()
    {
        // The same id under another subject: not the race the unique index of (Provider, Subject) settles, so nothing to report as "taken".
        await using var db = new TestDatabase();
        var repo = db.Get<IUserRepository>();
        var first = User.CreateExternal(UserProvider.Proxy, "sub-1", null, null, false);
        var sameId = User.CreateExternal(UserProvider.Proxy, "sub-2", null, null, false);
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(sameId, first.Id);
        await repo.AddAsync(first, default);

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => repo.AddAsync(sameId, default));
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
    public async Task ResetTokens_KnowWhenTheyWereIssued_AndTheStaleOnesAreDeleted()
    {
        await using var db = new TestDatabase();
        var alice = User.CreateLocal("a@x.co", null, false);
        var bob = User.CreateLocal("b@x.co", null, false);
        await db.Get<IUserRepository>().AddAsync(alice, default);
        await db.Get<IUserRepository>().AddAsync(bob, default);
        var repo = db.Get<IPasswordResetTokenRepository>();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var old = PasswordResetToken.Issue(alice.Id, "hash", now.AddDays(-3), TimeSpan.FromHours(1));
        var recent = PasswordResetToken.Issue(alice.Id, "hash", now.AddMinutes(-2), TimeSpan.FromHours(1));
        var bobs = PasswordResetToken.Issue(bob.Id, "hash", now.AddHours(-2), TimeSpan.FromHours(1));
        foreach (var t in new[] { old, recent, bobs }) await repo.AddAsync(t, default);

        Assert.Equal(recent.IssuedAt, (await repo.FindAsync(recent.Id, default))!.IssuedAt);
        Assert.Equal(recent.IssuedAt, await repo.LatestIssuedAtAsync(alice.Id, default));
        Assert.Null(await repo.LatestIssuedAtAsync(Guid.NewGuid(), default));

        Assert.Equal(1, await repo.DeleteStaleAsync(now.AddDays(-1), default));
        Assert.Null(await repo.FindAsync(old.Id, default));
        Assert.NotNull(await repo.FindAsync(recent.Id, default));
        Assert.NotNull(await repo.FindAsync(bobs.Id, default));
    }

    [Fact]
    public async Task ResetTokens_AreListedByUser_AndRemovedById()
    {
        await using var db = new TestDatabase();
        var alice = User.CreateLocal("a@x.co", null, false);
        var bob = User.CreateLocal("b@x.co", null, false);
        await db.Get<IUserRepository>().AddAsync(alice, default);
        await db.Get<IUserRepository>().AddAsync(bob, default);
        var repo = db.Get<IPasswordResetTokenRepository>();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var first = PasswordResetToken.Issue(alice.Id, "hash", now, TimeSpan.FromHours(1));
        var second = PasswordResetToken.Issue(alice.Id, "hash", now, TimeSpan.FromHours(1));
        var bobs = PasswordResetToken.Issue(bob.Id, "hash", now, TimeSpan.FromHours(1));
        foreach (var t in new[] { first, second, bobs }) await repo.AddAsync(t, default);

        Assert.Equal(new[] { first.Id, second.Id }.Order(), (await repo.IdsForUserAsync(alice.Id, default)).Order());
        Assert.Empty(await repo.IdsForUserAsync(Guid.NewGuid(), default));

        await repo.RemoveAsync([first.Id, Guid.NewGuid()], default);
        await repo.RemoveAsync([], default);

        Assert.Null(await repo.FindAsync(first.Id, default));
        Assert.NotNull(await repo.FindAsync(second.Id, default));
        Assert.NotNull(await repo.FindAsync(bobs.Id, default));
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
