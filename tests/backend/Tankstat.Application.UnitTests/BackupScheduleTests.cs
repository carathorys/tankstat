using System.Globalization;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Users;

namespace Tankstat.Application.UnitTests;

/// <summary>When a scheduled backup is due, also on the nights the clocks change, and when one was missed.</summary>
public class BackupScheduleTests
{
    private static readonly TimeOnly TwoAm = new(2, 0);
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2027, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void TheNextDueTime_IsTodayWhenItIsStillAhead_ElseTomorrow()
    {
        Assert.Equal(Utc(1, 10, 2), BackupSchedule.NextAfter(Utc(1, 10, 1, 59), TwoAm, TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 11, 2), BackupSchedule.NextAfter(Utc(1, 10, 2), TwoAm, TimeZoneInfo.Utc)); // exactly due: the next one is tomorrow
        Assert.Equal(Utc(1, 11, 2), BackupSchedule.NextAfter(Utc(1, 10, 23), TwoAm, TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 10, 2), BackupSchedule.LastDueAtOrBefore(Utc(1, 10, 2), TwoAm, TimeZoneInfo.Utc));
        Assert.Equal(Utc(1, 9, 2), BackupSchedule.LastDueAtOrBefore(Utc(1, 10, 1, 59), TwoAm, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ItIsTheServersLocalTime()
    {
        // 02:00 in Budapest is 01:00 UTC in winter and 00:00 UTC in summer.
        Assert.Equal(Utc(1, 10, 1), BackupSchedule.NextAfter(Utc(1, 9, 12), TwoAm, Budapest));
        Assert.Equal(Utc(7, 10, 0), BackupSchedule.NextAfter(Utc(7, 9, 12), TwoAm, Budapest));
    }

    [Fact]
    public void OnTheNightTheClocksSkipIt_ItIsDueAtTheFirstTimeThatExists_AndOnTheNightTheyRepeatIt_AtTheFirstOfTheTwo()
    {
        // 28 March 2027: 02:00 becomes 03:00 in Budapest, so 02:30 never happens; the clocks jump at 01:00 UTC.
        Assert.Equal(Utc(3, 28, 1), BackupSchedule.DueOn(new DateOnly(2027, 3, 28), new TimeOnly(2, 30), Budapest));
        // 31 October 2027: 03:00 becomes 02:00, so 02:30 happens twice (00:30 and 01:30 UTC): the first.
        Assert.Equal(Utc(10, 31, 0, 30), BackupSchedule.DueOn(new DateOnly(2027, 10, 31), new TimeOnly(2, 30), Budapest));
        // Either way a backup is due once on each of those days, and the day after goes back to normal.
        Assert.Equal(Utc(3, 29, 0, 30), BackupSchedule.NextAfter(Utc(3, 28, 1), new TimeOnly(2, 30), Budapest));
    }

    [Fact]
    public void ABackupIsMissed_WhenTheLastDueTimePassedWithoutOne_ANewInstallOrAnOlderOneIncluded()
    {
        Assert.False(BackupSchedule.Missed(Utc(1, 10, 2, 0), Utc(1, 10, 9), TwoAm, TimeZoneInfo.Utc)); // written at 02:00 today
        Assert.False(BackupSchedule.Missed(Utc(1, 9, 2, 5), Utc(1, 10, 1), TwoAm, TimeZoneInfo.Utc)); // yesterday's, and today's is not due yet
        Assert.True(BackupSchedule.Missed(Utc(1, 9, 2, 5), Utc(1, 10, 3), TwoAm, TimeZoneInfo.Utc)); // down over 02:00
        Assert.True(BackupSchedule.Missed(null, Utc(1, 10, 3), TwoAm, TimeZoneInfo.Utc)); // none yet
    }

    [Theory]
    [InlineData("02:00", 2, 0)]
    [InlineData("2:30", 2, 30)]
    [InlineData(" 23:59 ", 23, 59)]
    public void ATimeOfDay_IsParsed(string text, int hour, int minute) => Assert.Equal(new TimeOnly(hour, minute), BackupSchedule.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("02:00:00")]
    [InlineData("2 am")]
    public void AnythingElse_IsNoTime(string? text) => Assert.Null(BackupSchedule.Parse(text));
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
