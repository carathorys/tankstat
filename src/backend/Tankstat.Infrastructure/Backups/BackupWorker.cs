using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Application.Setup;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The schedule (<c>Backups:Every</c> on <c>Backups:Days</c>): a backup at each of those times of the server's local time, through the same one-at-a-time runner as
/// Back up now, so a run due while one is being written joins it instead of writing a second. At start, a due time that passed without a
/// backup since (<see cref="BackupSchedule.Missed"/>) is caught up at once. Without the setting it does nothing and says nothing. Nothing
/// in here may stop the app: a failure is logged and the schedule goes on at the next due time. The settings apply at once: a new time, or
/// none, is taken up as soon as it is saved (a missed backup is caught up only at the app's start), while a reload that leaves the schedule as
/// it was never moves the due time waited for. While the setup waits nothing is written (the instance is empty, and such a backup would be
/// offered as a restore point by the setup page itself); the first due time after the setup writes the first backup.
/// </summary>
internal sealed class BackupWorker(
    BackupRunner runner, IBackupStore store, IOptionsMonitor<BackupOptions> options, SetupStatus setup, TimeProvider clock, ILogger<BackupWorker> logger)
    : BackgroundService
{
    private readonly Lock _sleep = new();
    private DateTimeOffset? _sleepingUntil;
    private readonly List<(DateTimeOffset Due, TaskCompletionSource Done)> _waiters = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously); // the round's signal, completed by a reload of the settings

    /// <summary>
    /// Tests: completes once the worker sleeps until <paramref name="due"/> or later, so everything before it (a missed backup caught up, the
    /// run of the time before) is done and its timer exists: the clock may be moved on.
    /// </summary>
    internal Task WhenAsleepUntil(DateTimeOffset due)
    {
        lock (_sleep)
        {
            if (_sleepingUntil >= due) return Task.CompletedTask;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((due, done));
            return done.Task;
        }
    }

    private void FallAsleep(DateTimeOffset until)
    {
        List<TaskCompletionSource> woken;
        lock (_sleep)
        {
            _sleepingUntil = until;
            woken = [.. _waiters.Where(w => w.Due <= until).Select(w => w.Done)];
            _waiters.RemoveAll(w => w.Due <= until);
        }
        foreach (var done in woken) done.TrySetResult();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // BackgroundService runs all of this on the thread pool (.NET 10), so the start of the app does not wait for the catching up below.
        // The settings apply at once (the settings page saves them): every reload completes the round's signal, and the next round reads them
        // afresh. Any reload does (a save of another group too), so only what the settings say decides, never that the worker was woken.
        using var subscription = options.OnChange((_, _) => Volatile.Read(ref _changed).TrySetResult());
        var starting = true;
        string? announced = null; // the schedule last said, as its text
        var warned = false; // that no backup is ever removed, while that holds
        var unusable = false; // that the settings cannot be read, while that holds
        (string Schedule, string Zone, DateTimeOffset Due)? plan = null; // the due time waited for, kept while the schedule stays
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // A new signal before the settings are read, the exchange a full fence between the two: a reload from here on completes it,
                // and one before it is in what is read.
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Exchange(ref _changed, changed);
                BackupOptions o;
                try
                {
                    o = options.CurrentValue;
                }
                catch (Exception e) when (e is OptionsValidationException or InvalidOperationException)
                {
                    // Settings that stopped being usable since the start (an appsettings.json edited by hand reloads on its own): said once,
                    // and nothing is planned until a reload brings usable ones, which completes the signal (one whose settings fail does not).
                    // The binder's own words quote the value, so only the validator's are logged.
                    if (!unusable)
                        logger.LogWarning("The backup settings cannot be used, so no backup is scheduled until they are fixed: {Problems}",
                            e is OptionsValidationException invalid ? string.Join(" ", invalid.Failures) : "a value cannot be read as its type.");
                    unusable = true;
                    FallAsleep(DateTimeOffset.MaxValue); // asleep until a reload, as late as it gets (tests wait for it)
                    await changed.Task.WaitAsync(ct);
                    continue;
                }
                if (unusable) logger.LogInformation("The backup settings can be used again");
                unusable = false;
                var zone = clock.LocalTimeZone;
                if (o.Schedule is not { } schedule)
                {
                    if (announced is not null) logger.LogInformation("Backups are not scheduled any more");
                    announced = null;
                    warned = false;
                    plan = null;
                    starting = false;
                    await changed.Task.WaitAsync(ct);
                    continue;
                }
                var key = schedule.ToString();
                if (announced != key)
                {
                    logger.LogInformation("Backups are written {Days} at {Times} ({Zone})",
                        schedule.IsEveryDay ? "every day" : string.Join(", ", schedule.Days),
                        string.Join(", ", schedule.Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))), zone.Id);
                    announced = key;
                }
                // When it comes to hold, at the start or when retention is cleared while backups are scheduled, not again for a new time.
                if (!o.Pruning && !warned)
                    logger.LogWarning("Backups are scheduled and none are ever removed: set Backups:Keep or Backups:KeepDays, or the backups folder grows until the disk is full");
                warned = !o.Pruning;
                if (starting)
                {
                    starting = false;
                    await CatchUpAsync(schedule, zone);
                }

                // The due time waited for stays while the schedule does: a reload handled once the clock reached it (its timer about to
                // end, or late after the host was suspended) still writes that backup, at once. A new schedule is planned from now, so a time
                // that passed today waits for its next day.
                var now = clock.GetUtcNow();
                var next = plan is { } kept && kept.Schedule == key && kept.Zone == zone.Id ? kept.Due : schedule.NextAfter(now, zone);
                plan = (key, zone.Id, next);
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var sleep = Task.Delay(next > now ? next - now : TimeSpan.Zero, clock, wake.Token);
                FallAsleep(next);
                logger.LogDebug("The next backup is due at {Next}", next);
                await Task.WhenAny(sleep, changed.Task);
                if (changed.Task.IsCompleted)
                {
                    // Planned again with the settings as they are now, also when the due time came at the same moment: a reload that cleared or
                    // moved the time wins over it, and one that left it writes the backup in the next round.
                    await wake.CancelAsync();
                    continue;
                }
                await sleep;
                // A timer counts whole milliseconds on a clock of its own, and the clock of the day may be set back meanwhile: it can end before
                // the due time reads on the clock, and a backup stamped before it would leave the night missed (and could be written twice).
                for (var woke = clock.GetUtcNow(); woke < next; woke = clock.GetUtcNow())
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling((next - woke).TotalMilliseconds)), clock, ct);
                plan = null; // this due time is served: the next round plans the one after it
                if (setup.Pending) logger.LogInformation("The setup is not finished: the backup due at {Due} is not written", next);
                else await RunAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "The backup schedule failed; it goes on at the next due time");
                await Task.Delay(TimeSpan.FromMinutes(1), clock, ct); // never a loop that spins
            }
        }
    }

    /// <summary>At start: the due time that passed without a backup since is written at once.</summary>
    private async Task CatchUpAsync(BackupSchedule schedule, TimeZoneInfo zone)
    {
        try
        {
            if (setup.Pending)
            {
                logger.LogInformation("The setup is not finished: no backup is written until it is");
                return;
            }
            if (schedule.Missed(NewestOwn(), clock.GetUtcNow(), zone))
            {
                logger.LogInformation("The backup due at {Due} was not written: writing it now", schedule.LastDueAtOrBefore(clock.GetUtcNow(), zone));
                await RunAsync();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "The missed backup could not be caught up; the schedule goes on");
        }
    }

    private DateTimeOffset? NewestOwn()
    {
        try
        {
            return BackupRetention.NewestOwn(store.List());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "The backups folder could not be read; the missed backup is written now");
            return null;
        }
    }

    /// <summary>A run of the schedule; one Back up now started meanwhile is the backup, and is waited for.</summary>
    private async Task RunAsync()
    {
        if (!runner.Start()) logger.LogInformation("A backup is being written already: the scheduled one is that one");
        await runner.WhenIdleAsync();
    }
}
