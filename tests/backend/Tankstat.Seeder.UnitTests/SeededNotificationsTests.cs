using Microsoft.Extensions.Time.Testing;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Recurring;
using Tankstat.Seeder;

namespace Tankstat.Seeder.UnitTests;

/// <summary>The recurring expenses (whose reminders the app works out) and the access-change notifications the seeder makes.</summary>
public class SeededNotificationsTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static SeedOptions Options(int vehicles = 60, int trashed = 5, IntRange? recurring = null, int notifications = 15, int seed = 42) =>
        new(vehicles, trashed, new IntRange(5, 15), seed, true, null, null, null, recurring ?? new IntRange(0, 2), notifications);

    private static List<GeneratedVehicle> Generate(SeedOptions options) => new DataGenerator(Clock).Generate(options).ToList();

    private static RecurrenceState State(GeneratedVehicle g, RecurringExpense item) =>
        RecurrenceCalculator.Evaluate(item, Today, g.Refuelings.LastOrDefault()?.Odometer).State;

    [Fact]
    public void LiveVehicles_GetTheRequestedNumberOfSchedules_TrashedOnesNone()
    {
        var all = Generate(Options(recurring: new IntRange(1, 3)));

        Assert.All(all.Where(g => !g.Vehicle.IsDeleted), g => Assert.InRange(g.Recurring.Count, 1, 3));
        Assert.All(all.Where(g => g.Vehicle.IsDeleted), g => Assert.Empty(g.Recurring));
        Assert.All(all.SelectMany(g => g.Recurring.Select(r => (g, r))), x =>
        {
            Assert.Equal((Guid.Empty, x.g.Vehicle.Id), (x.r.OwnerId, x.r.VehicleId));
            Assert.Equal(x.g.Recurring.Count, x.g.Recurring.Select(r => r.Title).Distinct().Count()); // no title twice on a vehicle
        });
        Assert.Empty(Generate(Options(recurring: new IntRange(0, 0))).SelectMany(g => g.Recurring));
    }

    [Fact]
    public void Schedules_AreOverdue_DueSoon_OrUpcoming_SoThereAreRemindersToSee()
    {
        var all = Generate(Options(vehicles: 100));

        var states = all.SelectMany(g => g.Recurring.Select(r => State(g, r))).ToList();
        Assert.Contains(RecurrenceState.Overdue, states);
        Assert.Contains(RecurrenceState.DueSoon, states);
        Assert.Contains(RecurrenceState.Upcoming, states);
        Assert.All(all.SelectMany(g => g.Recurring), r => Assert.True(r.LastDoneDate <= Today)); // never done in the future
    }

    [Fact]
    public void OnlyVehiclesWithAReading_GetSchedulesThatCountDistance()
    {
        var all = Generate(Options(vehicles: 40) with { RefuelingsPerVehicle = new IntRange(0, 1), RecurringPerVehicle = new IntRange(5, 5) });

        Assert.All(all.Where(g => g.Refuelings.Count == 0).SelectMany(g => g.Recurring), r => Assert.Equal(RecurrenceKind.Time, r.Kind));
        Assert.Contains(all.SelectMany(g => g.Recurring), r => r.UsesDistance);
        Assert.All(all.SelectMany(g => g.Recurring).Where(r => r.UsesDistance), r => Assert.True(r.LastDoneOdometer >= 0));
    }

    [Fact]
    public void Notifications_GoToTheAnonymousUser_AreMixedReadAndUnread_AndStayWithinTheRetention()
    {
        var generator = new DataGenerator(Clock);
        var vehicles = Generate(Options()).Where(g => !g.Vehicle.IsDeleted).Select(g => g.Vehicle).Take(20).ToList();

        var notifications = generator.Notifications(Options(notifications: 30), vehicles);

        Assert.Equal(30, notifications.Count);
        Assert.All(notifications, n => Assert.Equal(Guid.Empty, n.RecipientId));
        Assert.Contains(notifications, n => n.IsRead);
        Assert.Contains(notifications, n => !n.IsRead);
        Assert.All(notifications.Where(n => n.IsRead), n => Assert.InRange(n.ReadAt!.Value, n.UpdatedAt, Clock.GetUtcNow()));
        Assert.All(notifications, n => Assert.True(n.CreatedAt > Clock.GetUtcNow().AddDays(-15))); // the last two weeks
        Assert.Contains(notifications, n => n.Count > 1); // some stand for several changes
        Assert.Single(notifications, n => n.Kind == NotificationKind.MoreActivity);
        Assert.All(notifications.Where(n => n.Context is { Type: NotificationEntityType.Vehicle }), n => Assert.Contains(vehicles, v => v.Id == n.ContextId));
        Assert.True(notifications.Select(n => n.Kind).Distinct().Count() >= 5);
    }

    [Fact]
    public void WithoutVehicles_OnlyNotificationsThatNeedNone_AreMade()
    {
        var notifications = new DataGenerator(Clock).Notifications(Options(notifications: 20), []);

        Assert.Equal(20, notifications.Count);
        Assert.All(notifications, n => Assert.Null(n.Context));
        Assert.DoesNotContain(notifications, n => n.Kind is NotificationKind.LogAccessChanged or NotificationKind.VehicleShared);
    }

    [Fact]
    public void SameSeed_SameSchedulesAndNotifications()
    {
        static string Fingerprint(SeedOptions options)
        {
            var generator = new DataGenerator(Clock);
            var all = generator.Generate(options).ToList();
            var schedules = all.SelectMany(g => g.Recurring.Select(r => $"{g.Vehicle.Name}:{r.Title}:{r.LastDoneDate}:{r.LastDoneOdometer}"));
            var notifications = generator.Notifications(options, [.. all.Select(g => g.Vehicle).Where(v => !v.IsDeleted)])
                .Select(n => $"{n.Kind}:{n.Count}:{n.ReadAt}:{string.Join(",", n.Args.Select(a => $"{a.Key}={a.Value}"))}");
            return string.Join("|", schedules.Concat(notifications));
        }

        Assert.Equal(Fingerprint(Options(seed: 5)), Fingerprint(Options(seed: 5)));
        Assert.NotEqual(Fingerprint(Options(seed: 5)), Fingerprint(Options(seed: 6)));
    }
}
