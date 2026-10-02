using Tankstat.Application.Auth;
using Tankstat.Application.Recurring;
using Tankstat.Domain.Access;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class NotificationServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 1); // the fake clock

    private sealed record Scene(World W, User Alice, User Bob, User Carol, Vehicle Car);

    private static async Task<Scene> Setup(AuthMode mode = AuthMode.Standalone)
    {
        var w = new World(mode);
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        var carol = w.AddUser("carol@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, default);
        return new Scene(w, alice, bob, carol, car);
    }

    private static List<Notification> Of(World w, User user) => w.Notifications.Items.Where(n => n.RecipientId == user.Id).ToList();

    /// <summary>Every year, last done <paramref name="lastDone"/> (so with the default 30 warning days: overdue before last October, due soon within it).</summary>
    private static RecurringExpenseInput Yearly(DateOnly lastDone, string title = "Insurance") =>
        new(title, null, null, RecurrenceKind.Time, 12, null, lastDone, null, null, null);

    private static readonly DateOnly OverdueStart = new(2025, 9, 1);
    private static readonly DateOnly DueSoonStart = new(2025, 10, 15);

    // ---- Access changes ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SharingAVehicle_TellsTheGrantee_ButNotTheOwnerWhoDidIt()
    {
        var s = await Setup();

        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);

        var n = Assert.Single(Of(s.W, s.Bob));
        Assert.Equal((NotificationKind.LogAccessChanged, NotificationRef.Vehicle(s.Car.Id), (NotificationRef?)NotificationRef.Vehicle(s.Car.Id)), (n.Kind, n.Subject, n.Context));
        Assert.Equal(("Car", s.Alice.DisplayName, "EDIT"), (n.Args["vehicleName"], n.Args["actorName"], n.Args["level"]));
        Assert.Empty(Of(s.W, s.Alice));
    }

    [Fact]
    public async Task ChangesBeforeTheGranteeLooked_AreFoldedIntoOne_AndUndoingThemRemovesIt()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);

        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Delete, default);
        var n = Assert.Single(Of(s.W, s.Bob));
        Assert.Equal(("DELETE", 2), (n.Args["level"], n.Count));

        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.None, default);
        Assert.Empty(Of(s.W, s.Bob)); // back to no access: nothing to tell
    }

    [Fact]
    public async Task AChangeAfterTheGranteeReadTheLastOne_IsANewNotification_AndSettingTheSameLevelIsNone()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        Of(s.W, s.Bob)[0].MarkRead(s.W.Clock.GetUtcNow());

        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default); // no change
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.None, default);
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Carol.Id, AccessLevel.None, default); // had nothing

        Assert.Equal(["EDIT", "NONE"], Of(s.W, s.Bob).Select(n => n.Args["level"]));
        Assert.Empty(Of(s.W, s.Carol));
    }

    [Fact]
    public async Task WhenSomeoneElseShares_TheOwnerIsToldWhoGotWhat()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit)); // Bob may edit (and so share) Alice's vehicles
        s.W.Current.SignInAs(s.Bob);

        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Carol.Id, AccessLevel.Edit, default);

        Assert.Equal(NotificationKind.LogAccessChanged, Assert.Single(Of(s.W, s.Carol)).Kind);
        var owner = Assert.Single(Of(s.W, s.Alice));
        Assert.Equal((NotificationKind.VehicleShared, NotificationRef.User(s.Carol.Id), (NotificationRef?)NotificationRef.Vehicle(s.Car.Id)), (owner.Kind, owner.Subject, owner.Context));
        Assert.Equal((s.Bob.DisplayName, s.Carol.DisplayName, "EDIT"), (owner.Args["actorName"], owner.Args["userName"], owner.Args["level"]));
        Assert.Empty(Of(s.W, s.Bob)); // nothing about what he did himself
    }

    [Fact]
    public async Task AnAdministratorsGrant_TellsTheGranteeAndTheOwner()
    {
        var s = await Setup();
        var root = s.W.AddUser("root@x.co", admin: true);
        s.W.Current.SignInAs(root);

        await s.W.AccessAdmin.SetGrantAsync(s.Alice.Id, s.Bob.Id, AccessLevel.View, default);

        var grantee = Assert.Single(Of(s.W, s.Bob));
        Assert.Equal((NotificationKind.DataAccessChanged, NotificationRef.User(s.Alice.Id), s.Alice.DisplayName, "VIEW"), (grantee.Kind, grantee.Subject, grantee.Args["userName"], grantee.Args["level"]));
        var owner = Assert.Single(Of(s.W, s.Alice));
        Assert.Equal((NotificationKind.DataShared, NotificationRef.User(s.Bob.Id), s.Bob.DisplayName), (owner.Kind, owner.Subject, owner.Args["userName"]));
        Assert.Empty(Of(s.W, root));
    }

    [Fact]
    public async Task ChangingTheDefaultLevel_TellsEveryEnabledUserItAffects_OnceEach()
    {
        var s = await Setup();
        var root = s.W.AddUser("root@x.co", admin: true);
        var otherAdmin = s.W.AddUser("admin2@x.co", admin: true);
        s.Carol.SetDisabled(true);
        s.W.Current.SignInAs(root);

        await s.W.AccessAdmin.SetDefaultLevelAsync(AccessLevel.View, default);
        await s.W.AccessAdmin.SetDefaultLevelAsync(AccessLevel.View, default); // unchanged

        Assert.Equal(new[] { s.Alice.Id, s.Bob.Id }.Order(), s.W.Notifications.Items.Select(n => n.RecipientId).Order());
        Assert.All(s.W.Notifications.Items, n => Assert.Equal((NotificationKind.DefaultAccessChanged, "VIEW"), (n.Kind, n.Args["level"])));
        Assert.Empty(Of(s.W, otherAdmin));
    }

    [Fact]
    public async Task PastTheHourlyLimit_FurtherEventsOnlyCountUp_UntilTheHourIsOver()
    {
        var s = await Setup();
        s.W.NotificationOptions.MaxPerHour = 2;
        var cars = new List<Vehicle>();
        for (var i = 0; i < 4; i++) cars.Add(await s.W.VehicleService.AddAsync($"Car {i}", null, FuelType.Petrol, default));

        foreach (var car in cars) await s.W.Sharing.SetLogAccessAsync(car.Id, s.Bob.Id, AccessLevel.Edit, default);

        Assert.Equal([NotificationKind.LogAccessChanged, NotificationKind.LogAccessChanged, NotificationKind.MoreActivity], Of(s.W, s.Bob).Select(n => n.Kind));
        Assert.Equal(2, Of(s.W, s.Bob)[2].Count);

        s.W.Clock.Advance(TimeSpan.FromHours(1));
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        Assert.Equal(NotificationKind.LogAccessChanged, Of(s.W, s.Bob)[^1].Kind);
    }

    // ---- Recurring expenses (worked out when the inbox is read) -----------------------------------------------------------

    [Fact]
    public async Task AnOverdueSchedule_IsNotifiedOnce_HoweverOftenTheInboxIsRead()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(OverdueStart), default)).Item;

        Assert.Equal(1, await s.W.NotificationService.CountAsync(unreadOnly: true, default));
        Assert.Equal(1, await s.W.NotificationService.CountAsync(unreadOnly: true, default));

        var n = Assert.Single(await s.W.NotificationService.ListAsync(false, 0, 10, default));
        Assert.Equal((NotificationKind.RecurringOverdue, NotificationRef.RecurringExpense(item.Id), (NotificationRef?)NotificationRef.Vehicle(s.Car.Id)), (n.Kind, n.Subject, n.Context));
        Assert.Equal(("Insurance", "Car"), (n.Args["title"], n.Args["vehicleName"]));
    }

    [Fact]
    public async Task ADueSoonSchedule_ThatBecomesOverdue_IsTheSameNotification_UnreadAgain()
    {
        var s = await Setup();
        await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(DueSoonStart), default);
        var dueSoon = Assert.Single(await s.W.NotificationService.ListAsync(false, 0, 10, default));
        Assert.Equal(NotificationKind.RecurringDueSoon, dueSoon.Kind);
        await s.W.NotificationService.MarkReadAsync(null, default);

        s.W.Clock.Advance(TimeSpan.FromDays(20));

        var overdue = Assert.Single(await s.W.NotificationService.ListAsync(false, 0, 10, default));
        Assert.Equal((dueSoon.Id, NotificationKind.RecurringOverdue, false), (overdue.Id, overdue.Kind, overdue.IsRead));
    }

    [Fact]
    public async Task UpcomingSchedules_AreNotNotified_AndTheNextCycleIsNotifiedAgain()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(OverdueStart), default)).Item;
        await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(Today, "Tax"), default); // a year away
        Assert.Equal(1, await s.W.NotificationService.CountAsync(false, default));

        await s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(new DateOnly(2025, 10, 20), null, false, null, null), default); // due soon again

        Assert.Equal([NotificationKind.RecurringDueSoon, NotificationKind.RecurringOverdue], (await s.W.NotificationService.ListAsync(false, 0, 10, default)).Select(n => n.Kind).Order());
    }

    [Fact]
    public async Task EveryoneWhoCanSeeTheSchedule_IsNotified_ButAnAdministratorOnlyForWhatConcernsThem()
    {
        var s = await Setup();
        await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(OverdueStart), default);
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        var root = s.W.AddUser("root@x.co", admin: true);

        int Count(User user)
        {
            s.W.Current.SignInAs(user);
            return s.W.NotificationService.ListAsync(false, 0, 10, default).Result.Count(n => n.Topic == NotificationTopic.Recurring);
        }

        Assert.Equal((1, 1, 0, 0), (Count(s.Alice), Count(s.Bob), Count(s.Carol), Count(root)));

        s.W.Settings.Value.SetDefaultLevelForOthers(AccessLevel.View); // now everyone may see Alice's logs
        Assert.Equal((1, 1), (Count(s.Carol), Count(root)));
    }

    [Fact]
    public async Task WithAuthenticationOff_TheAnonymousUserIsNotified()
    {
        var s = await Setup(AuthMode.None);
        s.W.Current.Principal = null;
        var car = await s.W.VehicleService.AddAsync("Van", null, FuelType.Diesel, default);
        await s.W.RecurringService.AddAsync(car.Id, Yearly(OverdueStart), default);

        Assert.Equal(1, await s.W.NotificationService.CountAsync(true, default));
        Assert.Equal(Principal.Anonymous.Id, Assert.Single(s.W.Notifications.Items).RecipientId);
    }

    [Fact]
    public async Task TheSchedulesOfATrashedVehicle_AreNotNotified()
    {
        var s = await Setup();
        await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(OverdueStart), default);
        await s.W.VehicleService.DeleteAsync(s.Car.Id, default);

        Assert.Equal(0, await s.W.NotificationService.CountAsync(false, default));
    }

    // ---- The inbox ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheInbox_IsTheRecipientsAlone_AndRemembersWhenItWasRead()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        var bobs = Assert.Single(Of(s.W, s.Bob));

        // Alice (and administrators alike) can neither read nor mark Bob's notification.
        Assert.Empty(await s.W.NotificationService.ListAsync(false, 0, 10, default));
        Assert.Empty(await s.W.NotificationService.MarkReadAsync([bobs.Id], default));
        Assert.False(bobs.IsRead);

        s.W.Current.SignInAs(s.Bob);
        Assert.Equal([bobs.Id], (await s.W.NotificationService.MarkReadAsync([bobs.Id], default)).Select(n => n.Id));
        Assert.Equal(s.W.Clock.GetUtcNow(), bobs.ReadAt);
        Assert.Empty(await s.W.NotificationService.MarkReadAsync(null, default)); // nothing left unread
        Assert.Equal(0, await s.W.NotificationService.CountAsync(unreadOnly: true, default));
    }

    [Fact]
    public async Task ReadNotifications_AreRemovedAfterTheRetentionPeriod_UnreadOnesStay()
    {
        var s = await Setup();
        s.W.NotificationOptions.ReadRetentionDays = 30;
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        var van = await s.W.VehicleService.AddAsync("Van", null, FuelType.Diesel, default);
        await s.W.Sharing.SetLogAccessAsync(van.Id, s.Bob.Id, AccessLevel.Edit, default);
        s.W.Current.SignInAs(s.Bob);
        await s.W.NotificationService.MarkReadAsync([Of(s.W, s.Bob)[0].Id], default);

        s.W.Clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(2, await s.W.NotificationService.CountAsync(false, default)); // read exactly 30 days ago: still kept

        s.W.Clock.Advance(TimeSpan.FromMinutes(1));
        var left = await s.W.NotificationService.ListAsync(false, 0, 10, default);
        Assert.Equal(["Van"], left.Select(n => n.Args["vehicleName"])); // the unread one stays however old
    }

    [Fact]
    public async Task AReadReminder_OfAScheduleThatIsStillDue_IsKept_SoItDoesNotComeBackAsNew()
    {
        var s = await Setup();
        var item = (await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(OverdueStart), default)).Item;
        Assert.Equal(1, await s.W.NotificationService.CountAsync(true, default));
        await s.W.NotificationService.MarkReadAsync(null, default);

        s.W.Clock.Advance(TimeSpan.FromDays(60));
        var kept = Assert.Single(await s.W.NotificationService.ListAsync(false, 0, 10, default));
        Assert.True(kept.IsRead);

        await s.W.RecurringService.MarkDoneAsync(item.Id, new MarkDoneInput(DateOnly.FromDateTime(s.W.Clock.GetUtcNow().UtcDateTime), null, false, null, null), default);
        Assert.Empty(await s.W.NotificationService.ListAsync(false, 0, 10, default)); // done: the old reminder can go
    }

    [Fact]
    public async Task PagingIsClamped()
    {
        var s = await Setup();
        await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(OverdueStart), default);
        await s.W.RecurringService.AddAsync(s.Car.Id, Yearly(DueSoonStart, "Tax"), default);

        Assert.Equal(2, (await s.W.NotificationService.ListAsync(false, -5, 1000, default)).Count);
        Assert.Single(await s.W.NotificationService.ListAsync(false, 0, 0, default)); // at least one per page
    }

    [Fact]
    public async Task ReadingTheInbox_NeedsSomeoneSignedIn()
    {
        var s = await Setup();
        s.W.Current.Principal = null;

        await Assert.ThrowsAsync<UnauthenticatedException>(() => s.W.NotificationService.CountAsync(false, default));
    }
}
