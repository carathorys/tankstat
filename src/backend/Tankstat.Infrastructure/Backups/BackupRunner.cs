using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Writes backups one at a time: a run asked for while one is under way is not started twice. A run goes on in the background, so the
/// request that asked for it answers at once (a backup with photos can take longer than any request may), and it ends with the app: the
/// stopping token cancels it, and the host waits for it to wind down (its partial file and temporary copy removed) before it stops, so a
/// restart in the process never starts the next host under a run of the last one. Outside a host (the command line's start-up steps, tests)
/// there is no lifetime to end a run, and nothing starts one there.
/// </summary>
internal sealed class BackupRunner(IBackupWriter writer, TimeProvider clock, ILogger<BackupRunner> logger, IHostApplicationLifetime? lifetime = null)
    : IBackupRunner, IHostedService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    // One value, replaced whole, so a reader never pairs "running" with another run's details.
    private volatile BackupRunStatus _status = new(false, null);
    private volatile Task _current = Task.CompletedTask;

    public BackupRunStatus Status => _status;

    public bool Start()
    {
        if (!_gate.Wait(0)) return false;
        var started = clock.GetUtcNow();
        _status = new BackupRunStatus(true, new BackupRun(started, null, null, null, null));
        _current = Task.Run(() => RunAsync(started, lifetime?.ApplicationStopping ?? CancellationToken.None));
        return true;
    }

    /// <summary>Tests: waits for the run under way, if any.</summary>
    internal Task WhenIdleAsync() => _current;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The stopping token has cancelled the run by now: waits for it to wind down, within the host's own time to stop, and never throws.</summary>
    public async Task StopAsync(CancellationToken cancellationToken) =>
        await _current.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    private async Task RunAsync(DateTimeOffset started, CancellationToken ct)
    {
        BackupRun? run = null;
        try
        {
            var result = await writer.WriteAsync(new BackupRequest(), ct);
            run = new BackupRun(started, clock.GetUtcNow(), Path.GetFileName(result.Path), new FileInfo(result.Path).Length, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("The backup under way was stopped with the app");
            run = new BackupRun(started, clock.GetUtcNow(), null, null, "backup.stopped");
        }
        catch (Exception e)
        {
            if (e is KeyedException keyed) logger.LogWarning(e, "A backup failed: {Key}", keyed.Key);
            else logger.LogError(e, "A backup failed");
            run = new BackupRun(started, clock.GetUtcNow(), null, null, (e as KeyedException)?.Key ?? "backup.failed");
        }
        finally
        {
            // Before the gate opens: the next run's status, set as it starts, is never overwritten by this one's.
            _status = new BackupRunStatus(false, run ?? new BackupRun(started, clock.GetUtcNow(), null, null, "backup.failed"));
            _gate.Release();
        }
    }
}
