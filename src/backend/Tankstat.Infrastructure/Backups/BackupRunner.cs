using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Writes backups one at a time: a run asked for while one is under way is not started twice (Back up now and the schedule share it). A run
/// goes on in the background, so the request that asked for it answers at once (a backup with photos can take longer than any request may),
/// and it ends with the app. After a backup is written, retention removes what it says; a run that failed (not one the app's stop cut short:
/// the next start finds that one missed) notifies the administrators.
/// </summary>
/// <param name="lifetime">The app's, whose stop cuts a run short; null where there is no host (the services built on their own, as tests do).</param>
internal sealed class BackupRunner(
    IBackupWriter writer, IBackupStore store, IOptionsMonitor<BackupOptions> options, IServiceScopeFactory scopes, TimeProvider clock, ILogger<BackupRunner> logger,
    IHostApplicationLifetime? lifetime = null) : IBackupRunner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile BackupRun? _last;
    private volatile bool _running;
    private volatile Task _current = Task.CompletedTask;

    public BackupRunStatus Status => new(_running, _last);

    public bool Start(Guid? actorId = null)
    {
        if (!_gate.Wait(0)) return false;
        _running = true;
        _current = Task.Run(() => RunAsync(actorId, lifetime?.ApplicationStopping ?? CancellationToken.None));
        return true;
    }

    /// <summary>The run under way, if any, to wait for (the schedule joins one Back up now started; tests wait for it).</summary>
    internal Task WhenIdleAsync() => _current;

    private async Task RunAsync(Guid? actorId, CancellationToken ct)
    {
        var started = clock.GetUtcNow();
        try
        {
            var result = await writer.WriteAsync(new BackupRequest(), ct);
            _last = new BackupRun(started, clock.GetUtcNow(), Path.GetFileName(result.Path), new FileInfo(result.Path).Length, null);
            Prune();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("The backup under way was stopped with the app");
            _last = new BackupRun(started, clock.GetUtcNow(), null, null, "backup.stopped");
        }
        catch (Exception e)
        {
            if (e is KeyedException keyed) logger.LogWarning(e, "A backup failed: {Key}", keyed.Key);
            else logger.LogError(e, "A backup failed");
            _last = new BackupRun(started, clock.GetUtcNow(), null, null, (e as KeyedException)?.Key ?? "backup.failed");
            await NotifyAsync(_last, actorId);
        }
        finally
        {
            _running = false;
            _gate.Release();
        }
    }

    /// <summary>Retention, after a backup was written; a file that cannot be removed is a Warning and the rest still goes.</summary>
    private void Prune()
    {
        var o = options.CurrentValue;
        if (!o.Pruning) return;
        try
        {
            foreach (var point in BackupRetention.ToRemove(store.List(), o.Keep, o.KeepDays, clock.GetUtcNow()))
            {
                try
                {
                    store.Delete(point.File);
                    logger.LogInformation("Removed the backup {File} (retention)", point.File);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(e, "The backup {File} could not be removed", point.File);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "The backups folder could not be read to prune it");
        }
    }

    /// <summary>In a scope of its own (the notifications are stored per request otherwise), and never cancelled: a failure near the app's stop is still told.</summary>
    private async Task NotifyAsync(BackupRun run, Guid? actorId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BackupFailureNotifier>().NotifyAsync(run, actorId, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "The administrators could not be told that a backup failed");
        }
    }
}
