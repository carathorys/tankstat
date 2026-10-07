using Tankstat.Application.Users;
using Tankstat.Domain.Users;

namespace Tankstat.Infrastructure.UnitTests;

public class UserSessionRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private static async Task<User> AddUser(TestDatabase db, string email)
    {
        var user = User.CreateLocal(email, null, false);
        await db.Get<IUserRepository>().AddAsync(user, default);
        return user;
    }

    [Fact]
    public async Task ASession_RoundTrips_AndARotationIsSaved()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUserSessionRepository>();
        var alice = await AddUser(db, "alice@x.co");
        var session = UserSession.Issue(alice.Id, 2, "HASH", "SEALED", "Firefox on Linux", Now, Lifetime);
        await repo.AddAsync(session, default);

        session.Rotate("NEW", "SEALED-NEW", Now.AddDays(1), Lifetime);
        await repo.UpdateAsync(session, default);
        var loaded = (await repo.FindAsync(session.Id, default))!;

        Assert.Equal(("NEW", "SEALED-NEW", "HASH", 2, "Firefox on Linux"), (loaded.SecretHash, loaded.ProtectedSecret, loaded.PreviousSecretHash, loaded.SessionVersion, loaded.Client));
        Assert.Equal((Now.AddDays(1), Now.AddDays(91)), (loaded.RotatedAt!.Value, loaded.ExpiresAt));
    }

    [Fact]
    public async Task EndingAUsersSessions_KeepsTheOneAsked_AndOtherUsers()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUserSessionRepository>();
        var alice = await AddUser(db, "alice@x.co");
        var bob = await AddUser(db, "bob@x.co");
        var keep = UserSession.Issue(alice.Id, 0, "A1", "S", null, Now, Lifetime);
        var end = UserSession.Issue(alice.Id, 0, "A2", "S", null, Now, Lifetime);
        var bobs = UserSession.Issue(bob.Id, 0, "B1", "S", null, Now, Lifetime);
        foreach (var s in new[] { keep, end, bobs }) await repo.AddAsync(s, default);

        Assert.Equal(1, await repo.RevokeForUserAsync(alice.Id, keep.Id, Now, default));
        Assert.Equal(1, await repo.RevokeForUserAsync(alice.Id, null, Now.AddHours(1), default)); // the one kept before
        Assert.Equal(0, await repo.RevokeForUserAsync(alice.Id, null, Now.AddHours(2), default)); // nothing left: when it ended stays

        Assert.Equal(Now, (await repo.FindAsync(end.Id, default))!.RevokedAt);
        Assert.Null((await repo.FindAsync(bobs.Id, default))!.RevokedAt);
    }

    [Fact]
    public async Task StaleSessions_AreDeleted_LiveOnesStay()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUserSessionRepository>();
        var alice = await AddUser(db, "alice@x.co");
        var expired = UserSession.Issue(alice.Id, 0, "E", "S", null, Now.AddDays(-100), Lifetime);
        var ended = UserSession.Issue(alice.Id, 0, "R", "S", null, Now, Lifetime);
        ended.Revoke(Now.AddDays(-8));
        var live = UserSession.Issue(alice.Id, 0, "L", "S", null, Now, Lifetime);
        foreach (var s in new[] { expired, ended, live }) await repo.AddAsync(s, default);

        Assert.Equal(2, await repo.DeleteStaleAsync(Now.AddDays(-7), default));

        Assert.NotNull(await repo.FindAsync(live.Id, default));
    }

    [Fact]
    public async Task DeletingAUser_DeletesTheirSessions()
    {
        await using var db = new TestDatabase();
        var alice = await AddUser(db, "alice@x.co");
        var admin = User.CreateLocal("admin@x.co", null, true);
        await db.Get<IUserRepository>().AddAsync(admin, default);
        var session = UserSession.Issue(alice.Id, 0, "H", "S", null, Now, Lifetime);
        await db.Get<IUserSessionRepository>().AddAsync(session, default);

        await db.Get<IUserDataRepository>().DeleteUserAsync(alice.Id, null, default);

        Assert.Null(await db.Get<IUserSessionRepository>().FindAsync(session.Id, default));
    }
}
