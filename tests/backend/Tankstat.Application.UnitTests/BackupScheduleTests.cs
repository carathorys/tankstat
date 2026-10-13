using System.Globalization;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Users;

namespace Tankstat.Application.UnitTests;

/// <summary>When a scheduled backup is due, on the chosen days at each time, also on the nights the clocks change, and when one was missed.</summary>
public class BackupScheduleTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
    private static readonly BackupSchedule TwoAm = BackupSchedule.Of("02:00", null)!;
    private static readonly BackupSchedule HalfPastTwo = BackupSchedule.Of("02:30", null)!;

    // 2027-01-10 is a Sunday.
    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2027, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void TheNextDueTime_IsTodayWhenItIsStillAhead_ElseTomorrow()
    {
        Assert.Equal(Utc(1, 10, 2), TwoAm.NextAfter(Utc(1, 10, 1, 59), TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 11, 2), TwoAm.NextAfter(Utc(1, 10, 2), TimeZoneInfo.Utc)); // exactly due: the next one is tomorrow
        Assert.Equal(Utc(1, 11, 2), TwoAm.NextAfter(Utc(1, 10, 23), TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 10, 2), TwoAm.LastDueAtOrBefore(Utc(1, 10, 2), TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 9, 2), TwoAm.LastDueAtOrBefore(Utc(1, 10, 1, 59), TimeZoneInfo.Utc));
    }

    [Fact]
    public void SeveralTimes_AreEachDue_InTheirOrder()
    {
        var twice = BackupSchedule.Of("14:00, 2:00", null)!;

        Assert.Equal([new TimeOnly(2, 0), new TimeOnly(14, 0)], twice.Times);
        Assert.Equal(Utc(1, 10, 14), twice.NextAfter(Utc(1, 10, 2), TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 11, 2), twice.NextAfter(Utc(1, 10, 14), TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 10, 14), twice.LastDueAtOrBefore(Utc(1, 10, 23), TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 9, 14), twice.LastDueAtOrBefore(Utc(1, 10, 1), TimeZoneInfo.Utc));
    }

    [Fact]
    public void OnlyTheChosenDays_AreDue()
    {
        var mondaysAndThursdays = BackupSchedule.Of("02:00", "thursday, Mon")!;

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Thursday], mondaysAndThursdays.Days);
        Assert.Equal(Utc(1, 11, 2), mondaysAndThursdays.NextAfter(Utc(1, 10, 3), TimeZoneInfo.Utc)); // Sunday: the next is Monday
        Assert.Equal(Utc(1, 14, 2), mondaysAndThursdays.NextAfter(Utc(1, 11, 2), TimeZoneInfo.Utc)); // then Thursday
        Assert.Equal(Utc(1, 7, 2), mondaysAndThursdays.LastDueAtOrBefore(Utc(1, 10, 3), TimeZoneInfo.Utc)); // the Thursday before
        Assert.False(mondaysAndThursdays.Missed(Utc(1, 7, 2, 5), Utc(1, 10, 23), TimeZoneInfo.Utc)); // nothing was due since
        Assert.True(mondaysAndThursdays.Missed(Utc(1, 7, 2, 5), Utc(1, 11, 3), TimeZoneInfo.Utc));
    }

    [Fact]
    public void TheDays_AreTheServersLocalDays()
    {
        // 00:30 on Monday in Budapest is still Sunday in UTC.
        var mondays = BackupSchedule.Of("00:30", "Monday")!;
        Assert.Equal(Utc(1, 10, 23, 30), mondays.NextAfter(Utc(1, 9, 12), Budapest));
    }

    [Fact]
    public void ItIsTheServersLocalTime()
    {
        // 02:00 in Budapest is 01:00 UTC in winter and 00:00 UTC in summer.
        Assert.Equal(Utc(1, 10, 1), TwoAm.NextAfter(Utc(1, 9, 12), Budapest));
        Assert.Equal(Utc(7, 10, 0), TwoAm.NextAfter(Utc(7, 9, 12), Budapest));
    }

    [Fact]
    public void OnTheNightTheClocksSkipIt_ItIsDueAtTheFirstTimeThatExists_AndOnTheNightTheyRepeatIt_AtTheFirstOfTheTwo()
    {
        // 28 March 2027: 02:00 becomes 03:00 in Budapest, so 02:30 never happens; the clocks jump at 01:00 UTC.
        Assert.Equal(Utc(3, 28, 1), BackupSchedule.DueOn(new DateOnly(2027, 3, 28), new TimeOnly(2, 30), Budapest));
        // 31 October 2027: 03:00 becomes 02:00, so 02:30 happens twice (00:30 and 01:30 UTC): the first.
        Assert.Equal(Utc(10, 31, 0, 30), BackupSchedule.DueOn(new DateOnly(2027, 10, 31), new TimeOnly(2, 30), Budapest));
        // Either way a backup is due once on each of those days, and the day after goes back to normal.
        Assert.Equal(Utc(3, 28, 1), HalfPastTwo.NextAfter(Utc(3, 27, 12), Budapest));
        Assert.Equal(Utc(3, 29, 0, 30), HalfPastTwo.NextAfter(Utc(3, 28, 1), Budapest));
        Assert.Equal(Utc(11, 1, 1, 30), HalfPastTwo.NextAfter(Utc(10, 31, 0, 30), Budapest));
    }

    [Fact]
    public void TheNextAndTheLastDueTime_AreTheNearestInstants_AlsoWhereADaysFirstTimeComesBeforeTheDayBeforesLast()
    {
        // Asunción's spring night of 2017: the clocks went from 23:59 (-04:00) to 01:00 (-03:00), and .NET may read 00:15 of the new day
        // as 00:15-03:00, an instant before 23:30-04:00 of the day before it. Whichever way the platform reads that night, the next due
        // time is the earlier instant and the last one the later, never the first of a day that has one.
        var asuncion = TimeZoneInfo.FindSystemTimeZoneById("America/Asuncion");
        var schedule = BackupSchedule.Of("23:30, 00:15", "Saturday,Sunday")!;
        DateTimeOffset[] dues = [BackupSchedule.DueOn(new DateOnly(2017, 9, 30), new TimeOnly(23, 30), asuncion), BackupSchedule.DueOn(new DateOnly(2017, 10, 1), new TimeOnly(0, 15), asuncion)];

        Assert.Equal(dues.Min(), schedule.NextAfter(new DateTimeOffset(2017, 10, 1, 2, 0, 0, TimeSpan.Zero), asuncion));
        Assert.Equal(dues.Max(), schedule.LastDueAtOrBefore(dues.Max().AddMinutes(1), asuncion));
    }

    [Fact]
    public void ABackupIsMissed_WhenTheLastDueTimePassedWithoutOne_ANewInstallOrAnOlderOneIncluded()
    {
        Assert.False(TwoAm.Missed(Utc(1, 10, 2, 0), Utc(1, 10, 9), TimeZoneInfo.Utc)); // written at 02:00 today
        Assert.False(TwoAm.Missed(Utc(1, 9, 2, 5), Utc(1, 10, 1), TimeZoneInfo.Utc)); // yesterday's, and today's is not due yet
        Assert.True(TwoAm.Missed(Utc(1, 9, 2, 5), Utc(1, 10, 3), TimeZoneInfo.Utc)); // down over 02:00
        Assert.True(TwoAm.Missed(null, Utc(1, 10, 3), TimeZoneInfo.Utc)); // none yet
    }

    [Fact]
    public void NoTime_OrNoDay_IsNoSchedule()
    {
        Assert.Null(BackupSchedule.Of(null, null));
        Assert.Null(BackupSchedule.Of(" ", "Monday"));
        Assert.Null(BackupSchedule.Of("02:00", BackupSchedule.NoDay));
        Assert.Null(BackupSchedule.Of("02:00", "someday"));
        Assert.True(BackupSchedule.Of("02:00", BackupSchedule.EveryDay)!.IsEveryDay);
        Assert.True(BackupSchedule.Of("02:00", "")!.IsEveryDay); // unset: every day
    }

    [Theory]
    [InlineData("02:00", "02:00")]
    [InlineData("2:30", "02:30")]
    [InlineData(" 23:59 ", "23:59")]
    [InlineData("14:00,02:00;14:00 06:15", "02:00 06:15 14:00")]
    [InlineData("", "")]
    [InlineData("02:00\n", "02:00")] // a secret or a file read with its line break
    [InlineData("\t2:30\r\n", "02:30")]
    [InlineData("02:00 ", "02:00")]
    [InlineData("02:00,\t14:00", "02:00 14:00")]
    [InlineData("\t", "")] // whitespace alone: nothing, as unset
    public void TimesOfDay_AreParsed(string text, string expected) =>
        Assert.Equal(expected, string.Join(" ", BackupSchedule.ParseTimes(text)!.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))));

    [Theory]
    [InlineData("24:00")]
    [InlineData("02:00:00")]
    [InlineData("2 am")]
    [InlineData("02:00, later")]
    public void AnythingElse_IsNoTime(string text) => Assert.Null(BackupSchedule.ParseTimes(text));

    [Theory]
    [InlineData("Monday,Friday", "Monday Friday")]
    [InlineData("fri MON mon", "Monday Friday")]
    [InlineData("None", "")]
    [InlineData(null, "Monday Tuesday Wednesday Thursday Friday Saturday Sunday")]
    [InlineData("Monday,Friday\n", "Monday Friday")]
    [InlineData("None\r\n", "")]
    [InlineData("\t", "Monday Tuesday Wednesday Thursday Friday Saturday Sunday")]
    public void DaysOfTheWeek_AreParsed(string? text, string expected) => Assert.Equal(expected, string.Join(" ", BackupSchedule.ParseDays(text)!));

    [Theory]
    [InlineData("Mo")]
    [InlineData("None,Monday")]
    [InlineData("Hétfő")]
    public void AnythingElse_IsNoDay(string text) => Assert.Null(BackupSchedule.ParseDays(text));
}

/// <summary>Which backups go after the app wrote one: both settings are ceilings, the newest always stays, and only the app's own files count.</summary>
public class BackupRetentionTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static RestorePoint Point(string file, int daysAgo, bool safetyNet = false, string writtenBy = BackupManifest.Tankstat) => new(file, 100, new BackupManifest(
        1, writtenBy, "0.13.0", Now.AddDays(-daysAgo), "Sqlite", "AddInstance", safetyNet, new BackupContents(new Dictionary<string, long>(), true, BackupPhotos.WithPictures, false),
        new BackupSizes(0, 0, 0, 0, 0), new BackupArchive(BackupArchive.Plain, ""), null));

    private static RestorePoint Own(int daysAgo, bool safetyNet = false) => Point($"tankstat-backup-day{daysAgo:00}.zip", daysAgo, safetyNet);

    private static string[] Removed(IReadOnlyList<RestorePoint> points, int? keep, int? keepDays) =>
        [.. BackupRetention.ToRemove(points, keep, keepDays, Now).Select(p => p.File).Order()];

    [Fact]
    public void WithNeitherSetting_NothingIsRemoved_SafetyNetsIncluded()
    {
        var points = Enumerable.Range(0, 10).Select(d => Own(d)).Concat(Enumerable.Range(20, 6).Select(d => Own(d, safetyNet: true))).ToList();

        Assert.Empty(Removed(points, null, null));
    }

    [Fact]
    public void Keep_LeavesTheNewestThatMany()
    {
        var points = new[] { Own(3), Own(0), Own(2), Own(1) };

        Assert.Equal(["tankstat-backup-day02.zip", "tankstat-backup-day03.zip"], Removed(points, keep: 2, keepDays: null));
        Assert.Equal(["tankstat-backup-day01.zip", "tankstat-backup-day02.zip", "tankstat-backup-day03.zip"], Removed(points, keep: 1, keepDays: null));
    }

    [Fact]
    public void KeepDays_RemovesTheOlderOnes_ButNeverTheNewest()
    {
        Assert.Equal(["tankstat-backup-day40.zip"], Removed([Own(1), Own(10), Own(40)], keep: null, keepDays: 30));
        Assert.Empty(Removed([Own(90)], keep: null, keepDays: 30)); // the only one is the newest: it stays, however old
        Assert.Equal(["tankstat-backup-day80.zip"], Removed([Own(60), Own(80)], keep: null, keepDays: 30));
    }

    [Fact]
    public void KeepDays_ReachingBackBeforeTheCalendarStarts_IsNoLimitByAge()
    {
        // 9999999 days would be about 27,000 years before now: nothing is that old, and working out the date must not fail the backup.
        Assert.Empty(Removed([Own(0), Own(400)], keep: null, keepDays: 9_999_999));
        Assert.Equal(["tankstat-backup-day400.zip"], Removed([Own(0), Own(400)], keep: 1, keepDays: int.MaxValue));
    }

    [Fact]
    public void BothSettings_AreCeilings_ABackupGoesWhenItIsBeyondEither()
    {
        var points = Enumerable.Range(0, 10).Select(d => Own(d * 5)).ToList(); // 0, 5, ..., 45 days old

        // keep the newest 5 (0..20 days) and none older than 12 days (0, 5, 10): the stricter of the two wins for each file
        Assert.Equal(["tankstat-backup-day15.zip", "tankstat-backup-day20.zip", "tankstat-backup-day25.zip", "tankstat-backup-day30.zip", "tankstat-backup-day35.zip", "tankstat-backup-day40.zip", "tankstat-backup-day45.zip"],
            Removed(points, keep: 5, keepDays: 12));
    }

    [Fact]
    public void OnlyTheAppsOwnFilesAreCandidates_NeverAnUploadOrAFileCopiedInUnderAnotherName()
    {
        var points = new[]
        {
            Own(0), Own(50),
            Point("uploaded-old-server.zip", 60), // an upload
            Point("before-the-move.zip", 70), // copied in by hand, a Tankstat backup under another name
            Point("tankstat-backup-from-elsewhere.zip", 80, writtenBy: "another-tool"),
        };

        Assert.Equal(["tankstat-backup-day50.zip"], Removed(points, keep: 1, keepDays: 1));
    }

    [Fact]
    public void SafetyNets_KeepACountOfTheirOwn_AndDoNotCountAsTheNewestBackup()
    {
        var points = new[] { Own(0, safetyNet: true), Own(1, safetyNet: true), Own(2, safetyNet: true), Own(3, safetyNet: true), Own(4, safetyNet: true), Own(5), Own(6) };

        Assert.Equal(["tankstat-backup-day03.zip", "tankstat-backup-day04.zip", "tankstat-backup-day06.zip"], Removed(points, keep: 1, keepDays: null));
        Assert.Equal(new DateTimeOffset(2027, 1, 26, 12, 0, 0, TimeSpan.Zero), BackupRetention.NewestOwn(points)); // the newest backup, not the newest safety net
        Assert.Null(BackupRetention.NewestOwn([Point("uploaded-a.zip", 0), Own(0, safetyNet: true)]));
    }
}

/// <summary>A failed backup is told to every enabled administrator once a day, never to the one who pressed Back up now; with sign-in off to the anonymous user.</summary>
public class BackupFailureNotifierTests
{
    private static BackupFailureNotifier Notifier(World world, AuthMode mode = AuthMode.Standalone) =>
        new(world.Users, world.Notifier, Options.Create(new AuthOptions { Mode = mode }), world.Clock);

    private static BackupRun Failed(DateTimeOffset at, string error = "backup.failed") => new(at, at.AddSeconds(3), null, null, error);

    [Fact]
    public async Task EveryEnabledAdministrator_IsToldOnceADay_WithTheReasonAndTheTime()
    {
        var world = new World();
        world.Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        var (a, b, disabled, user) = (User.CreateLocal("a@x.co", "A", true), User.CreateLocal("b@x.co", "B", true), User.CreateLocal("c@x.co", "C", true), User.CreateLocal("d@x.co", "D", false));
        disabled.SetDisabled(true);
        world.Users.Items.AddRange([a, b, disabled, user]);
        var night = new DateTimeOffset(2027, 1, 10, 2, 0, 0, TimeSpan.Zero);

        await Notifier(world).NotifyAsync(Failed(night, "backup.corrupt"), null, default);
        await Notifier(world).NotifyAsync(Failed(night.AddHours(5)), null, default); // the same day again: nothing new
        await Notifier(world).NotifyAsync(Failed(night.AddDays(1)), null, default); // the next night: a new one

        var told = world.Notifications.Items;
        Assert.Equal(new[] { a.Id, a.Id, b.Id, b.Id }.Order(), told.Select(n => n.RecipientId).Order());
        Assert.All(told, n => Assert.Equal((NotificationKind.BackupFailed, NotificationTopic.Backup, NotificationRef.Instance), (n.Kind, n.Topic, n.Subject)));
        var first = told.First(n => n.RecipientId == a.Id && n.Occurrence == "2027-01-10");
        Assert.Equal(("backup.corrupt", night.ToString("O")), (first.Args["error"], first.Args["startedAt"]));
        Assert.False(first.Args.ContainsKey("errorArgs")); // a reason without values says nothing more
        Assert.Contains(told, n => n.Occurrence == "2027-01-11");
    }

    [Fact]
    public async Task TheValuesOfTheReason_GoAlong_AndTheDayIsTheSameInEveryCulture()
    {
        var world = new World();
        world.Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        world.Users.Items.Add(User.CreateLocal("a@x.co", "A", true));
        var night = new DateTimeOffset(2027, 1, 10, 2, 0, 0, TimeSpan.Zero);
        var run = Failed(night, "backup.databaseNewer") with { ErrorArgs = new Dictionary<string, object?> { ["migration"] = "AddSomethingNew" } };

        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH"); // a calendar of another era
        try
        {
            await Notifier(world).NotifyAsync(run, null, default);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        var told = Assert.Single(world.Notifications.Items);
        Assert.Equal(("""{"migration":"AddSomethingNew"}""", "2027-01-10"), (told.Args["errorArgs"], told.Occurrence));
    }

    [Fact]
    public async Task TheAdministratorWhoPressedBackUpNow_IsLeftOut()
    {
        var world = new World();
        var (me, other) = (User.CreateLocal("a@x.co", "A", true), User.CreateLocal("b@x.co", "B", true));
        world.Users.Items.AddRange([me, other]);

        await Notifier(world).NotifyAsync(Failed(world.Clock.GetUtcNow()), me.Id, default);

        Assert.Equal([other.Id], world.Notifications.Items.Select(n => n.RecipientId));
    }

    [Fact]
    public async Task WithSignInOff_TheAnonymousUserIsTold()
    {
        var world = new World();
        world.Users.Items.Add(User.CreateLocal("a@x.co", "A", true)); // an administrator of another mode: not who is signed in now

        await Notifier(world, AuthMode.None).NotifyAsync(Failed(world.Clock.GetUtcNow()), null, default);

        Assert.Equal([Principal.Anonymous.Id], world.Notifications.Items.Select(n => n.RecipientId));
    }
}
