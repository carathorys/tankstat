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
        // One per person: a run still under way (two starts at once, from two tabs) gives way, never left running out of reach of Cancel.
        _byOwner.AddOrUpdate(job.Record.OwnerId, running, (_, earlier) =>
        {
            StopRun(earlier);
            return running;
        });
        running.Task = Task.Run(() => RunAsync(running));
    }

    public bool Cancel(Guid ownerId)
    {
        if (!_byOwner.TryGetValue(ownerId, out var running)) return false;
        StopRun(running);
        return true;
    }

    public void Forget(Guid ownerId)
    {
        if (!_byOwner.TryGetValue(ownerId, out var running)) return;
        running.Forgotten = true;
        StopRun(running);
    }

    /// <summary>Asks a run to stop; one that ended meanwhile has disposed of its token source and needs nothing.</summary>
    private static void StopRun(Running running)
    {
        try
        {
            running.Stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
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
            var now = clock.GetUtcNow();
            record = record with { State = ExportState.Ready, ReadyAt = now, KeptUntil = now + options.CurrentValue.Keep, Bytes = written.Bytes };
            files.Save(record);
            // Written and recorded: the person's earlier export goes now, never before; a newer one being built (it could not stop this one in
            // time) stays. One that cannot be deleted now is no failure of this one: the sweep or the next export takes it.
            try
            {
                foreach (var old in files.Records().Where(r => r.OwnerId == record.OwnerId && r.Id != record.Id && r.State != ExportState.Building))
                {
                    files.DeleteFile(old.Id);
                    files.DeleteRecord(old.Id);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Export {ExportId} is ready, but the earlier export could not be deleted ({Failure})", record.Id, e.GetType().Name);
            }
            logger.LogInformation("User {UserId} exported {Cars} cars in export {ExportId}: {Bytes} bytes, {Encryption}",
                record.OwnerId, written.Cars, record.Id, written.Bytes, record.Encrypted ? "encrypted" : "in plain");
            if (!running.Forgotten) await TellAsync(n => n.ReadyAsync(record, CancellationToken.None), record);
        }
        // A stop, whatever it surfaced as (SqlClient reports a cancelled read as a SqlException).
        catch (Exception) when (running.Stop.IsCancellationRequested)
        {
            var interrupted = lifetime?.ApplicationStopping.IsCancellationRequested == true;
            DeleteOwnFile(record.Id); // a stop that came after the file was moved into place: nothing of it is kept either
            files.Save(record with { State = interrupted ? ExportState.Interrupted : ExportState.Cancelled });
            logger.LogInformation("Export {ExportId} of user {UserId} was {End}; nothing of it was kept", record.Id, record.OwnerId, interrupted ? "interrupted" : "cancelled");
        }
        catch (Exception e)
        {
            var key = e is KeyedException keyed ? keyed.Key : "export.failed";
            record = record with { State = ExportState.Failed, FailureKey = key };
            DeleteOwnFile(record.Id); // a failure after the file was moved into place (its record not saved) leaves no file the sweep never meets
            files.Save(record);
            // The type only: a database error quotes values, here someone's cars.
            logger.LogError("Export {ExportId} of user {UserId} failed ({Failure}); nothing of it was kept", record.Id, record.OwnerId, e.GetType().Name);
            logger.LogDebug(e, "Export {ExportId}: the failure", record.Id);
            if (!running.Forgotten) await TellAsync(n => n.FailedAsync(record, CancellationToken.None), record); // a deleted account is told nothing
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

    private void DeleteOwnFile(Guid id)
    {
        try
        {
            files.DeleteFile(id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Export {ExportId} ended without a file, but what it wrote could not be deleted ({Failure})", id, e.GetType().Name);
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
