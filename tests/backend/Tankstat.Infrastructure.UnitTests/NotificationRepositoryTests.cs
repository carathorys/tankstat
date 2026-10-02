using Tankstat.Application.Notifications;
using Tankstat.Application.Users;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Users;

namespace Tankstat.Infrastructure.UnitTests;

public class NotificationRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly NotificationRef Car = NotificationRef.Vehicle(Guid.NewGuid());

    private static Notification Due(Guid recipient, Guid scheduleId, string cycle = "c1", NotificationKind kind = NotificationKind.RecurringDueSoon, DateTimeOffset? at = null) =>
        Notification.Create(recipient, kind, NotificationRef.RecurringExpense(scheduleId), Car, cycle,
            new Dictionary<string, string> { ["title"] = "Oil change", ["vehicleName"] = "Car" }, null, at ?? Now);

    private static Notification Shared(Guid recipient, NotificationRef? context, DateTimeOffset? at = null) =>
        Notification.Create(recipient, NotificationKind.LogAccessChanged, Car, context, Guid.NewGuid().ToString("N"),
            new Dictionary<string, string> { ["level"] = "EDIT" }, "NONE", at ?? Now);

    [Fact]
    public async Task ANotification_IsStoredAndLoadedBack_WithEveryField()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var n = Due(Alice, Guid.NewGuid());
        await repo.AddAsync(n, default);

        var loaded = (await repo.FindAsync(Alice, n.Id, default))!;

        Assert.Equal((NotificationKind.RecurringDueSoon, NotificationTopic.Recurring, n.Subject, n.Context, "c1"), (loaded.Kind, loaded.Topic, loaded.Subject, loaded.Context, loaded.Occurrence));
        Assert.Equal(new Dictionary<string, string> { ["title"] = "Oil change", ["vehicleName"] = "Car" }, loaded.Args);
        Assert.Equal((1, Now, Now, (DateTimeOffset?)null), (loaded.Count, loaded.CreatedAt, loaded.UpdatedAt, loaded.ReadAt));
        Assert.Null(await repo.FindAsync(Bob, n.Id, default)); // only for its recipient
    }

    [Fact]
    public async Task TheSameDerivedOccurrence_IsStoredOnlyOnce()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var schedule = Guid.NewGuid();

        Assert.True(await repo.AddAsync(Due(Alice, schedule), default));
        Assert.False(await repo.AddAsync(Due(Alice, schedule), default));
        Assert.True(await repo.AddAsync(Due(Alice, schedule, "c2"), default)); // the next cycle
        Assert.True(await repo.AddAsync(Due(Bob, schedule), default)); // someone else

        Assert.Equal(2, await repo.CountAsync(Alice, false, default));
    }

    [Fact]
    public async Task Changes_AreSaved()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var n = Due(Alice, Guid.NewGuid());
        await repo.AddAsync(n, default);

        n.Raise(NotificationKind.RecurringOverdue, new Dictionary<string, string> { ["title"] = "Oil" }, Now.AddDays(1));
        await repo.UpdateAsync(n, default);

        var loaded = (await repo.FindAsync(Alice, n.Id, default))!;
        Assert.Equal((NotificationKind.RecurringOverdue, "Oil", Now.AddDays(1)), (loaded.Kind, loaded.Args["title"], loaded.UpdatedAt));
    }

    [Fact]
    public async Task List_IsTheRecipientsOwn_LatestChangeFirst_AndPaged()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var old = Due(Alice, Guid.NewGuid(), at: Now.AddDays(-2));
        var mid = Due(Alice, Guid.NewGuid(), at: Now.AddDays(-1));
        var recent = Due(Alice, Guid.NewGuid());
        foreach (var n in new[] { old, recent, mid, Due(Bob, Guid.NewGuid()) }) await repo.AddAsync(n, default);
        await repo.MarkReadAsync(Alice, [recent.Id], Now, default);

        Assert.Equal([recent.Id, mid.Id, old.Id], (await repo.ListAsync(Alice, false, 0, 10, default)).Select(n => n.Id));
        Assert.Equal([mid.Id], (await repo.ListAsync(Alice, false, 1, 1, default)).Select(n => n.Id));
        Assert.Equal([mid.Id, old.Id], (await repo.ListAsync(Alice, true, 0, 10, default)).Select(n => n.Id));
        Assert.Equal((3, 2), (await repo.CountAsync(Alice, false, default), await repo.CountAsync(Alice, true, default)));
    }

    [Fact]
    public async Task FindOpen_FindsOnlyAnUnreadOneAboutTheSameThing_InTheSameContext()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var read = Shared(Alice, Car, Now.AddMinutes(-5));
        var open = Shared(Alice, Car);
        var otherContext = Shared(Alice, null);
        foreach (var n in new[] { read, open, otherContext, Shared(Bob, Car) }) await repo.AddAsync(n, default);
        await repo.MarkReadAsync(Alice, [read.Id], Now, default);

        Assert.Equal(open.Id, (await repo.FindOpenAsync(Alice, NotificationTopic.LogAccess, Car, Car, default))?.Id);
        Assert.Equal(otherContext.Id, (await repo.FindOpenAsync(Alice, NotificationTopic.LogAccess, Car, null, default))?.Id);
        Assert.Null(await repo.FindOpenAsync(Alice, NotificationTopic.VehicleSharing, Car, Car, default));
    }

    [Fact]
    public async Task ListForSubjects_FindsReadAndUnreadOnesOfTheTopic()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var (oil, tyres) = (Guid.NewGuid(), Guid.NewGuid());
        var a = Due(Alice, oil);
        var b = Due(Alice, tyres);
        foreach (var n in new[] { a, b, Due(Alice, Guid.NewGuid()), Due(Bob, oil) }) await repo.AddAsync(n, default);
        await repo.MarkReadAsync(Alice, [a.Id], Now, default);

        var found = await repo.ListForSubjectsAsync(Alice, NotificationTopic.Recurring, [oil, tyres], default);

        Assert.Equal(new[] { a.Id, b.Id }.Order(), found.Select(n => n.Id).Order());
        Assert.Empty(await repo.ListForSubjectsAsync(Alice, NotificationTopic.LogAccess, [oil], default));
    }

    [Fact]
    public async Task CountCreatedSince_CountsTheRecipientsNewOnes_OfTheGivenTopics()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        foreach (var n in new[] { Shared(Alice, Car, Now.AddHours(-2)), Shared(Alice, Car, Now.AddMinutes(-30)), Shared(Alice, Car), Shared(Bob, Car), Due(Alice, Guid.NewGuid()) })
            await repo.AddAsync(n, default);

        Assert.Equal(2, await repo.CountCreatedSinceAsync(Alice, Now.AddHours(-1), [NotificationTopic.LogAccess, NotificationTopic.Digest], default));
    }

    [Fact]
    public async Task MarkRead_AndDeleteRead_TouchOnlyTheRecipientsOwn()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var mine = Due(Alice, Guid.NewGuid());
        var theirs = Due(Bob, Guid.NewGuid());
        foreach (var n in new[] { mine, Due(Alice, Guid.NewGuid()), theirs }) await repo.AddAsync(n, default);

        Assert.Equal(0, await repo.MarkReadAsync(Alice, [theirs.Id], Now, default));
        Assert.Equal(1, await repo.MarkReadAsync(Alice, [mine.Id], Now, default));
        Assert.Equal(1, await repo.MarkReadAsync(Alice, null, Now, default)); // the rest
        Assert.Equal(0, await repo.MarkReadAsync(Alice, null, Now, default));

        Assert.Equal(2, await repo.DeleteReadAsync(Alice, default));
        Assert.Equal((0, 1), (await repo.CountAsync(Alice, false, default), await repo.CountAsync(Bob, false, default)));
    }

    [Fact]
    public async Task Remove_DeletesIt_AndToleratesOneThatIsAlreadyGone()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<INotificationRepository>();
        var n = Due(Alice, Guid.NewGuid());
        await repo.AddAsync(n, default);

        await repo.RemoveAsync(n, default);
        await repo.RemoveAsync(n, default);

        Assert.Equal(0, await repo.CountAsync(Alice, false, default));
    }

    [Fact]
    public async Task DeletingAUser_DeletesTheirNotifications_EvenWhenTheirDataIsMoved()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var alice = User.CreateLocal("alice@x.co", null, false);
        var bob = User.CreateLocal("bob@x.co", null, false);
        await users.AddAsync(alice, default);
        await users.AddAsync(bob, default);
        var repo = db.Get<INotificationRepository>();
        await repo.AddAsync(Due(alice.Id, Guid.NewGuid()), default);
        await repo.AddAsync(Due(bob.Id, Guid.NewGuid()), default);

        await db.Get<IUserDataRepository>().DeleteUserAsync(alice.Id, bob.Id, default);

        Assert.Equal((0, 1), (await repo.CountAsync(alice.Id, false, default), await repo.CountAsync(bob.Id, false, default)));
    }
}
