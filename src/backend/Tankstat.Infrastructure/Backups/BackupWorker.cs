using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// The schedule (<c>Backups:Every</c>): a backup every day at that time of the server's local time, through the same one-at-a-time runner as
/// Back up now, so a run due while one is being written joins it instead of writing a second. At start, a due time that passed without a
/// backup since (<see cref="BackupSchedule.Missed"/>) is caught up at once. Without the setting it does nothing and says nothing. Nothing
/// in here may stop the app: a failure is logged and the schedule goes on at the next due time. The settings apply at once: a new time, or
/// none, is taken up as soon as it is saved (a missed backup is caught up only at the app's start).
/// </summary>
internal sealed class BackupWorker(BackupRunner runner, IBackupStore store, IOptionsMonitor<BackupOptions> options, TimeProvider clock, ILogger<BackupWorker> logger)
    : BackgroundService
{
    private readonly Lock _sleep = new();
    private DateTimeOffset? _sleepingUntil;
    private readonly List<(DateTimeOffset Due, TaskCompletionSource Done)> _waiters = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously); // set by a change of the settings

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
        await Task.Yield(); // the start of the app does not wait for the catching up below
        // The settings apply at once (the settings page saves them): every change wakes the worker, which reads them afresh.
        using var subscription = options.OnChange((_, _) => _changed.TrySetResult());
        var starting = true;
        TimeOnly? announced = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var changed = _changed.Task; // taken before the settings are read, so a change made meanwhile is not missed
                var o = options.CurrentValue;
                var zone = clock.LocalTimeZone;
                if (o.DailyAt is not { } at)
                {
                    if (announced is not null) logger.LogInformation("Backups are not scheduled any more");
                    announced = null;
                    starting = false;
                    await changed.WaitAsync(ct);
                    Rearm();
                    continue;
                }
                if (announced != at)
                {
                    logger.LogInformation("Backups are written every day at {Time} ({Zone})", at.ToString("HH:mm"), zone.Id);
                    if (!o.Pruning)
                        logger.LogWarning("Backups are scheduled and none are ever removed: set Backups:Keep or Backups:KeepDays, or the backups folder grows until the disk is full");
                    announced = at;
                }
                if (starting)
                {
                    starting = false;
                    await CatchUpAsync(at, zone);
                }

                var now = clock.GetUtcNow();
                var next = BackupSchedule.NextAfter(now, at, zone);
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var sleep = Task.Delay(next - now, clock, wake.Token);
                FallAsleep(next);
                logger.LogDebug("The next backup is due at {Next}", next);
                if (await Task.WhenAny(sleep, changed) == changed)
                {
                    await wake.CancelAsync();
                    Rearm();
                    continue; // planned again with the new settings
                }
                await sleep;
                await RunAsync();
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

    private void Rearm() => _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>At start: the due time that passed without a backup since is written at once.</summary>
    private async Task CatchUpAsync(TimeOnly at, TimeZoneInfo zone)
    {
        try
        {
            if (BackupSchedule.Missed(NewestOwn(), clock.GetUtcNow(), at, zone))
            {
                logger.LogInformation("The backup due at {Due} was not written: writing it now", BackupSchedule.LastDueAtOrBefore(clock.GetUtcNow(), at, zone));
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
