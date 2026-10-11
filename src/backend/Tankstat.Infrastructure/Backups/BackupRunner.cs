using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Writes backups one at a time: a run asked for while one is under way is not started twice. A run goes on in the background, so the
/// request that asked for it answers at once (a backup with photos can take longer than any request may), and it ends with the app.
/// </summary>
internal sealed class BackupRunner(IBackupWriter writer, TimeProvider clock, IHostApplicationLifetime lifetime, ILogger<BackupRunner> logger) : IBackupRunner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile BackupRun? _last;
    private volatile bool _running;
    private Task _current = Task.CompletedTask;

    public BackupRunStatus Status => new(_running, _last);

    public bool Start()
    {
        if (!_gate.Wait(0)) return false;
        _running = true;
        _current = Task.Run(() => RunAsync(lifetime.ApplicationStopping));
        return true;
    }

    /// <summary>Tests: waits for the run under way, if any.</summary>
    internal Task WhenIdleAsync() => _current;

    private async Task RunAsync(CancellationToken ct)
    {
        var started = clock.GetUtcNow();
        try
        {
            var result = await writer.WriteAsync(new BackupRequest(), ct);
            _last = new BackupRun(started, clock.GetUtcNow(), Path.GetFileName(result.Path), new FileInfo(result.Path).Length, null);
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
        }
        finally
        {
            _running = false;
            _gate.Release();
        }
    }
}
