using Tankstat.Domain.Notifications;

namespace Tankstat.Domain.UnitTests;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Recipient = Guid.NewGuid();
    private static readonly NotificationRef Car = NotificationRef.Vehicle(Guid.NewGuid());
    private static readonly NotificationRef Oil = NotificationRef.RecurringExpense(Guid.NewGuid());

    private static Dictionary<string, string> Args(params (string Name, string Value)[] args) => args.ToDictionary(a => a.Name, a => a.Value);

    private static NotificationDraft Shared(string level, string? before = null) =>
        new(Recipient, NotificationKind.LogAccessChanged, Car, Car, Args(("level", level)), Before: before, After: level);

    private static NotificationDraft Due(NotificationKind kind, string cycle = "2026-01-15:50000") =>
        new(Recipient, kind, Oil, Car, Args(("title", "Oil change")), Occurrence: cycle);

    private static Notification Added(NotificationChange change) => Assert.IsType<NotificationChange.Add>(change).Notification;

    private static NotificationChange Decide(NotificationDraft draft, Notification? existing = null, int createdLastHour = 0, Notification? digest = null, int max = 20) =>
        NotificationPolicy.Decide(draft, existing, createdLastHour, digest, max, Now);

    [Fact]
    public void Create_TakesTheTopicFromTheKind_AndStartsUnreadWithOneEvent()
    {
        var n = Notification.Create(Recipient, NotificationKind.RecurringOverdue, Oil, Car, "c1", Args(("title", "Oil")), null, Now);

        Assert.Equal((NotificationTopic.Recurring, Oil, (NotificationRef?)Car, 1, false), (n.Topic, n.Subject, n.Context, n.Count, n.IsRead));
        Assert.Equal((Now, Now), (n.CreatedAt, n.UpdatedAt));
        Assert.Equal("Oil", n.Args["title"]);
    }

    [Fact]
    public void Create_WithoutAContext_StoresAnEmptyContextId()
    {
        var n = Notification.Create(Recipient, NotificationKind.DefaultAccessChanged, NotificationRef.Instance, null, "c1", Args(), null, Now);

        Assert.Null(n.Context);
        Assert.Equal(Guid.Empty, n.ContextId);
    }

    [Fact]
    public void Create_RejectsAMissingOccurrence_AndTooManyOrTooLongArguments()
    {
        Assert.Throws<ArgumentException>(() => Notification.Create(Recipient, NotificationKind.RecurringDueSoon, Oil, null, " ", Args(), null, Now));
        Assert.Throws<ArgumentException>(() => Notification.Create(Recipient, NotificationKind.RecurringDueSoon, Oil, null, "c", Args(("t", new string('x', 201))), null, Now));
        var many = Enumerable.Range(0, 11).Select(i => ($"a{i}", "x")).ToArray();
        Assert.Throws<ArgumentException>(() => Notification.Create(Recipient, NotificationKind.RecurringDueSoon, Oil, null, "c", Args(many), null, Now));
    }

    [Fact]
    public void MarkRead_KeepsTheFirstTime()
    {
        var n = Notification.Create(Recipient, NotificationKind.RecurringDueSoon, Oil, null, "c", Args(), null, Now);

        n.MarkRead(Now);
        n.MarkRead(Now.AddHours(1));

        Assert.Equal(Now, n.ReadAt);
    }

    [Fact]
    public void Fold_RefusesAKindOfAnotherTopic()
    {
        var n = Notification.Create(Recipient, NotificationKind.RecurringDueSoon, Oil, null, "c", Args(), null, Now);

        Assert.Throws<InvalidOperationException>(() => n.Fold(NotificationKind.LogAccessChanged, Args(), Now));
    }

    [Fact]
    public void ADerivedDraft_IsAddedWithItsOccurrence()
    {
        var n = Added(Decide(Due(NotificationKind.RecurringDueSoon)));

        Assert.Equal(("2026-01-15:50000", NotificationKind.RecurringDueSoon), (n.Occurrence, n.Kind));
    }

    [Fact]
    public void ADerivedDraft_OfTheSameOrALowerKind_ChangesNothing_EvenWhenTheStoredOneWasRead()
    {
        var overdue = Added(Decide(Due(NotificationKind.RecurringOverdue)));
        overdue.MarkRead(Now);

        Assert.IsType<NotificationChange.Nothing>(Decide(Due(NotificationKind.RecurringOverdue), overdue));
        Assert.IsType<NotificationChange.Nothing>(Decide(Due(NotificationKind.RecurringDueSoon), overdue));
        Assert.True(overdue.IsRead);
    }

    [Fact]
    public void ADerivedDraft_OfAMoreUrgentKind_RaisesTheStoredOne_AndMakesItUnreadAgain()
    {
        var dueSoon = Added(Decide(Due(NotificationKind.RecurringDueSoon)));
        dueSoon.MarkRead(Now);

        var change = Decide(Due(NotificationKind.RecurringOverdue), dueSoon);

        Assert.Same(dueSoon, Assert.IsType<NotificationChange.Update>(change).Notification);
        Assert.Equal((NotificationKind.RecurringOverdue, false, 1), (dueSoon.Kind, dueSoon.IsRead, dueSoon.Count));
    }

    [Fact]
    public void DerivedDrafts_IgnoreTheHourlyLimit()
    {
        var n = Added(Decide(Due(NotificationKind.RecurringDueSoon), createdLastHour: 100, max: 1));

        Assert.Equal(NotificationKind.RecurringDueSoon, n.Kind);
    }

    [Fact]
    public void AnEvent_IsAddedWithAFreshOccurrence_AndRemembersTheValueBefore()
    {
        var first = Added(Decide(Shared("EDIT", before: "NONE")));
        var second = Added(Decide(Shared("EDIT", before: "NONE")));

        Assert.NotEqual(first.Occurrence, second.Occurrence);
        Assert.Equal("NONE", first.Before);
    }

    [Fact]
    public void AnEvent_IsFoldedIntoTheUnreadOneAboutTheSameThing_WithTheLatestValues()
    {
        var open = Added(Decide(Shared("EDIT", before: "NONE")));

        var change = Decide(Shared("DELETE", before: "EDIT"), open);

        Assert.Same(open, Assert.IsType<NotificationChange.Update>(change).Notification);
        Assert.Equal(("DELETE", 2, "NONE"), (open.Args["level"], open.Count, open.Before));
    }

    [Fact]
    public void AnEvent_ThatUndoesTheChangeBeforeItWasRead_RemovesTheNotification()
    {
        var open = Added(Decide(Shared("EDIT", before: "NONE")));
        Decide(Shared("DELETE", before: "EDIT"), open);

        var change = Decide(Shared("NONE", before: "DELETE"), open);

        Assert.Same(open, Assert.IsType<NotificationChange.Remove>(change).Notification);
    }

    [Fact]
    public void PastTheHourlyLimit_EventsBecomeOneMoreActivityNotification_ThatCountsUp()
    {
        var digest = Added(Decide(Shared("EDIT"), createdLastHour: 20));
        Assert.Equal((NotificationKind.MoreActivity, NotificationRef.Instance, 1), (digest.Kind, digest.Subject, digest.Count));

        var change = Decide(Shared("EDIT"), createdLastHour: 21, digest: digest);

        Assert.Same(digest, Assert.IsType<NotificationChange.Update>(change).Notification);
        Assert.Equal(2, digest.Count);
    }

    [Fact]
    public void PastTheHourlyLimit_AnUnreadNotificationAboutTheSameThing_IsStillFoldedInto()
    {
        var open = Added(Decide(Shared("EDIT")));

        Assert.IsType<NotificationChange.Update>(Decide(Shared("DELETE"), open, createdLastHour: 50));
        Assert.Equal("DELETE", open.Args["level"]);
    }
}
