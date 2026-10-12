using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The schedule on a fake clock (UTC as the server's local time): a backup missed while the app was down is written at start, then one at
/// every due time, retention after each, a Back up now while the scheduled one is written joins it, and a failing backup tells the
/// administrators once a day and removes nothing.
/// </summary>
public sealed class BackupScheduleTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-backups");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2027, 1, 10, 3, 0, 0, TimeSpan.Zero)); // 03:00, the night's 02:00 passed

    public BackupScheduleTests() => _clock.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static DateTimeOffset At(int day, int hour) => new(2027, 1, day, hour, 0, 0, TimeSpan.Zero);

    private TestApp App(Dictionary<string, string?> extra, Action<IServiceCollection>? services = null)
    {
        var settings = new Dictionary<string, string?> { ["Backups:Path"] = _folder, ["Backups:Every"] = "02:00" };
        foreach (var (key, value) in extra)
        {
            if (value is null) settings.Remove(key); // left to the settings file, which the page may write
            else settings[key] = value;
        }
        var app = TestApp.Standalone(settings, s =>
        {
            s.AddSingleton<TimeProvider>(_clock);
            services?.Invoke(s);
        });
        _ = app.Factory.Services; // starts the host, and the worker with it
        return app;
    }

    private static BackupWorker Worker(TestApp app) => app.Factory.Services.GetServices<IHostedService>().OfType<BackupWorker>().Single();

    private string[] Files() => Directory.Exists(_folder) ? [.. Directory.GetFiles(_folder).Select(Path.GetFileName).Order()!] : [];

    [Fact]
    public async Task AMissedBackupIsWrittenAtStart_ThenOneAtEveryDueTime_AndRetentionKeepsTheNewest()
    {
        using var app = App(new() { ["Backups:Keep"] = "2" });
        var worker = Worker(app);

        await worker.WhenAsleepUntil(At(11, 2)).WaitAsync(Wait);
        Assert.Equal(["tankstat-backup-20270110T030000Z.zip"], Files()); // the night's, caught up as the app started

        _clock.SetUtcNow(At(11, 2));
        await worker.WhenAsleepUntil(At(12, 2)).WaitAsync(Wait);
        Assert.Equal(["tankstat-backup-20270110T030000Z.zip", "tankstat-backup-20270111T020000Z.zip"], Files());

        _clock.SetUtcNow(At(12, 2));
        await worker.WhenAsleepUntil(At(13, 2)).WaitAsync(Wait);
        Assert.Equal(["tankstat-backup-20270111T020000Z.zip", "tankstat-backup-20270112T020000Z.zip"], Files()); // Keep=2: the oldest went

        var lines = app.Log.From<BackupWorker>().ToList();
        Assert.Contains(lines, e => e.Level == LogLevel.Information && e.Values.TryGetValue("Time", out var t) && (string?)t == "02:00");
        Assert.DoesNotContain(lines, e => e.Level >= LogLevel.Warning); // retention is set: no warning about a folder that only grows
        Assert.Contains(app.Log.From<BackupRunner>(), e => e.Level == LogLevel.Information && (string?)e.Values["File"] == "tankstat-backup-20270110T030000Z.zip");
    }

    [Fact]
    public async Task AStartAfterTheNightsBackup_WritesNoneAtStart_AndWarnsWhenNothingIsEverRemoved()
    {
        using (var first = App([]))
            await Worker(first).WhenAsleepUntil(At(11, 2)).WaitAsync(Wait);
        Assert.Single(Files());

        _clock.SetUtcNow(At(10, 9)); // the same day, later: today's is written
        using var again = App([]);
        await Worker(again).WhenAsleepUntil(At(11, 2)).WaitAsync(Wait);

        Assert.Single(Files());
        Assert.Contains(again.Log.From<BackupWorker>(), e => e.Level == LogLevel.Warning);
    }

    /// <summary>A writer that waits until it is let go: the scheduled backup under way while Back up now is pressed.</summary>
    private sealed class HeldWriter(IBackupWriter real) : IBackupWriter
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        public async Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await real.WriteAsync(request, ct);
        }
    }

    [Fact]
    public async Task BackUpNow_WhileTheScheduledOneIsWritten_JoinsIt()
    {
        HeldWriter? held = null;
        using var app = App([], s => s.AddSingleton<IBackupWriter>(sp => held = new HeldWriter(ActivatorUtilities.CreateInstance<BackupWriter>(sp))));
        var admin = app.NewClient();
        await admin.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);
        held = (HeldWriter)app.Factory.Services.GetRequiredService<IBackupWriter>();
        await held.Started.Task.WaitAsync(Wait); // the missed one is being written

        Assert.False((await admin.Gql("mutation { runBackup }")).Data().GetProperty("runBackup").GetBoolean());
        held.Release.SetResult();
        await Worker(app).WhenAsleepUntil(At(11, 2)).WaitAsync(Wait);

        Assert.Equal(1, held.Calls);
        Assert.Single(Files());
    }

    private sealed class FailingWriter : IBackupWriter
    {
        public Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct) => throw new IOException("The disk is full.");
    }

    [Fact]
    public async Task AFailingBackup_TellsEveryAdministratorOnceADay_AndRemovesNothing()
    {
        Directory.CreateDirectory(_folder);
        using (var before = TestApp.Standalone(new() { ["Backups:Path"] = _folder })) // two older backups of the app, which Keep=1 would thin out
        {
            var writer = before.Factory.Services.GetRequiredService<IBackupWriter>();
            await writer.WriteAsync(new BackupRequest(), default);
            await writer.WriteAsync(new BackupRequest(), default);
        }
        var older = Files();
        Assert.Equal(2, older.Length);

        using var app = App(new() { ["Backups:Keep"] = "1" }, s => s.AddSingleton<IBackupWriter>(new FailingWriter()));
        var worker = Worker(app);
        await worker.WhenAsleepUntil(At(11, 2)).WaitAsync(Wait); // the missed one failed
        var people = await app.Users();
        var bob = people.BobId;
        await people.Admin.Gql("mutation($id: UUID!) { setUserAdmin(userId: $id, isAdmin: true) { id } }", new { id = bob });

        _clock.SetUtcNow(At(11, 2));
        await worker.WhenAsleepUntil(At(12, 2)).WaitAsync(Wait); // the next night's failed too

        // A day on, the sign-ins of yesterday have run out: each signs in again to read the inbox.
        async Task<List<System.Text.Json.JsonElement>> Inbox(string email, string password)
        {
            var client = app.NewClient();
            await client.LoginAs(email, password);
            return [.. (await client.Gql("{ notifications { kind args { name value } } }")).Data().GetProperty("notifications").EnumerateArray()
                .Where(n => n.GetProperty("kind").GetString() == "BACKUP_FAILED")];
        }
        var root = await Inbox(TestApp.AdminEmail, TestApp.AdminPassword);
        var bobs = await Inbox("bob@example.com", "bob-password-1");
        var alices = await Inbox("alice@example.com", "alice-password-1");
        Assert.Equal(2, root.Count); // one a night
        Assert.Single(bobs); // an administrator since the second night only
        Assert.Empty(alices); // not an administrator
        Assert.Contains(bobs[0].GetProperty("args").EnumerateArray(), a => a.GetProperty("name").GetString() == "error" && a.GetProperty("value").GetString() == "backup.failed");
        Assert.Equal(older, Files()); // nothing was pruned
    }

    [Fact]
    public async Task WithoutASchedule_TheWorkerSaysNothing_AndWritesNothing()
    {
        using var app = App(new() { ["Backups:Every"] = null });

        _clock.Advance(TimeSpan.FromDays(3));
        await ((BackupRunner)app.Factory.Services.GetRequiredService<IBackupRunner>()).WhenIdleAsync();

        Assert.Empty(Files());
        Assert.Empty(app.Log.From<BackupWorker>());
    }

    [Fact]
    public async Task ATimeSavedOnTheSettingsPage_SchedulesBackupsAtOnce_AndClearingItStopsThem()
    {
        using var app = App(new() { ["Backups:Every"] = null });
        var admin = app.NewClient();
        await admin.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);
        const string Save = "mutation($c: [SystemSettingChangeInput!]!) { updateSystemSettings(changes: $c) { restartPending } }";

        var saved = (await admin.Gql(Save, new { c = new object[] { new { key = "Backups:Every", value = "04:00" }, new { key = "Backups:Keep", value = "3" } } })).Data();

        Assert.False(saved.GetProperty("updateSystemSettings").GetProperty("restartPending").GetBoolean()); // the backups apply at once
        await Worker(app).WhenAsleepUntil(At(10, 4)).WaitAsync(Wait); // tonight's 04:00, planned without a restart
        Assert.Empty(Files()); // a time set while running catches nothing up: only a start does
        _clock.Advance(TimeSpan.FromHours(1));
        await Worker(app).WhenAsleepUntil(At(11, 4)).WaitAsync(Wait);
        Assert.Single(Files());

        await admin.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword); // the hour the clock moved outlived the access cookie
        (await admin.Gql(Save, new { c = new object[] { new { key = "Backups:Every", value = "" } } })).Data();
        _clock.Advance(TimeSpan.FromDays(2)); // the save woke the worker before the clock moved: tomorrow's 04:00 is gone
        await ((BackupRunner)app.Factory.Services.GetRequiredService<IBackupRunner>()).WhenIdleAsync();
        Assert.Single(Files());
    }
}
