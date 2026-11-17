using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Exports;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Exports;

/// <summary>
/// Builds exports in the background (#341, the owner's decision 1: always), several people's side by side (they only read), one per person:
/// a new one cancels the person's under way. When the file is written the person's earlier one is deleted (only then, so a failed or
/// cancelled export leaves it), the record says ready and until when it is kept, and the person and the administrators are told. A
/// failure or a cancel keeps nothing. Its progress is in memory, like a backup run's status; a restart leaves the record "building",
/// which the sweep then says was cut short. The closing notifications are a write: one that cannot be saved (told busy during a run on
/// SQLite, say) is a Warning, never a failed export.
/// </summary>
internal sealed class ExportRunner(
    IExportWriter writer, IExportFiles files, IServiceScopeFactory scopes, IOptionsMonitor<ExportOptions> options, TimeProvider clock, ILogger<ExportRunner> logger,
    IHostApplicationLifetime? lifetime = null) : IExportRunner, IHostedService
{
    private sealed class Running(ExportJob job, CancellationTokenSource stop)
    {
        public ExportJob Job { get; } = job;
        public CancellationTokenSource Stop { get; } = stop;
        public ExportProgress? Progress { get; set; }
        public Task Task { get; set; } = Task.CompletedTask;
        public volatile bool Forgotten;
    }

    private readonly ConcurrentDictionary<Guid, Running> _byOwner = new();

    public DateTimeOffset Since { get; } = clock.GetUtcNow();

    public void Start(ExportJob job)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime?.ApplicationStopping ?? CancellationToken.None);
        var running = new Running(job, stop);
        _byOwner[job.Record.OwnerId] = running;
        running.Task = Task.Run(() => RunAsync(running));
    }

    public bool Cancel(Guid ownerId)
    {
        if (!_byOwner.TryGetValue(ownerId, out var running)) return false;
        running.Stop.Cancel();
        return true;
    }

    public void Forget(Guid ownerId)
    {
        if (!_byOwner.TryGetValue(ownerId, out var running)) return;
        running.Forgotten = true;
        running.Stop.Cancel();
    }

    public ExportProgress? ProgressOf(Guid id) => _byOwner.Values.FirstOrDefault(r => r.Job.Record.Id == id)?.Progress;

    public bool IsRunning(Guid id) => _byOwner.Values.Any(r => r.Job.Record.Id == id);

    /// <summary>Waits for every export under way (tests; a stop of the host).</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_byOwner.Values.Select(r => r.Task));

    private async Task RunAsync(Running running)
    {
        var (job, record) = (running.Job, running.Job.Record);
        try
        {
            var written = await writer.WriteAsync(job.Request, p => running.Progress = p, running.Stop.Token);
            // Written: the person's earlier export goes now, never before.
            foreach (var old in files.Records().Where(r => r.OwnerId == record.OwnerId && r.Id != record.Id))
            {
                files.DeleteFile(old.Id);
                files.DeleteRecord(old.Id);
            }
            var now = clock.GetUtcNow();
            record = record with { State = ExportState.Ready, ReadyAt = now, KeptUntil = now + options.CurrentValue.Keep, Bytes = written.Bytes };
            files.Save(record);
            logger.LogInformation("User {UserId} exported {Cars} cars in export {ExportId}: {Bytes} bytes, {Encryption}",
                record.OwnerId, written.Cars, record.Id, written.Bytes, record.Encrypted ? "encrypted" : "in plain");
            if (!running.Forgotten) await TellAsync(n => n.ReadyAsync(record, CancellationToken.None), record);
        }
        // A stop, whatever it surfaced as (SqlClient reports a cancelled read as a SqlException).
        catch (Exception) when (running.Stop.IsCancellationRequested)
        {
            var interrupted = lifetime?.ApplicationStopping.IsCancellationRequested == true;
            files.Save(record with { State = interrupted ? ExportState.Interrupted : ExportState.Cancelled });
            logger.LogInformation("Export {ExportId} of user {UserId} was {End}; nothing of it was kept", record.Id, record.OwnerId, interrupted ? "interrupted" : "cancelled");
        }
        catch (Exception e)
        {
            var key = e is KeyedException keyed ? keyed.Key : "export.failed";
            record = record with { State = ExportState.Failed, FailureKey = key };
            files.Save(record);
            // The type only: a database error quotes values, here someone's cars.
            logger.LogError("Export {ExportId} of user {UserId} failed ({Failure}); nothing of it was kept", record.Id, record.OwnerId, e.GetType().Name);
            logger.LogDebug(e, "Export {ExportId}: the failure", record.Id);
            await TellAsync(n => n.FailedAsync(record, CancellationToken.None), record);
        }
        finally
        {
            if (running.Forgotten)
            {
                // The account was deleted meanwhile: nothing of its export stays, however the run ended.
                files.DeleteFile(record.Id);
                files.DeleteRecord(record.Id);
            }
            _byOwner.TryRemove(new KeyValuePair<Guid, Running>(record.OwnerId, running));
            running.Stop.Dispose();
        }
    }

    private async Task TellAsync(Func<ExportNotifier, Task> tell, ExportRecord record)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await tell(scope.ServiceProvider.GetRequiredService<ExportNotifier>());
        }
        catch (Exception e)
        {
            logger.LogWarning("Export {ExportId} ended, but who is told of it could not be saved ({Failure})", record.Id, e.GetType().Name);
        }
    }

    /// <summary>At start: what a stopped server left (a record still building, half-written files) is put right, and files past their time go.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ExportService>().Sweep();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(e, "The exports' folder could not be swept at start");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var running in _byOwner.Values) running.Stop.Cancel();
        await WhenIdleAsync().WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
