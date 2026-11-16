using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Imports;
using Tankstat.Application.Runs;
using Tankstat.Application.Users;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Runs;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Imports;

/// <summary>
/// The queue of imports from a backup (#337, the owner's decisions 3 and 6): one run at a time on the instance, first come first
/// served, the others wait and start by themselves; one that waits longer than half an hour gives up. A run's file is on the server
/// that took its upload, so each server starts only the runs it holds, and only when the row says it is that run's turn
/// (<see cref="IImportRunRepository.ClaimAsync"/>, one statement). A run whose server is gone (it stopped saying it is there) never
/// starts: it ends as "did not finish" for whichever server notices, as does a running one whose mark is no longer open.
/// </summary>
internal sealed class BackupImportRunner(
    IServiceScopeFactory scopes, IImportRunRepository runs, IBackupImportSessions sessions, IRunMarkerRepository markers, IRunScope scope, RunGateState gate,
    IOptions<DatabaseOptions> database, IOptions<AuthOptions> auth, IOptionsMonitor<RunOptions> runOptions, TimeProvider clock, ILogger<BackupImportRunner> logger)
    : BackgroundService, IBackupImportRunner
{
    public const string NotOnMySqlKey = "import.notOnMySql";
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan KeepReports = TimeSpan.FromDays(7);
    /// <summary>A server that has not said it is there for this long is gone, and its runs with it.</summary>
    public static readonly TimeSpan GoneAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(5);

    private sealed class Held(Guid userId, string token, IDisposable? hold)
    {
        public Guid UserId { get; } = userId;
        public string Token { get; } = token;
        public IDisposable? Hold { get; set; } = hold;
        public ImportRunProgress? Progress { get; set; }
        public Guid? CancelledBy { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, Held> _held = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private Guid? _running;
    private DateTimeOffset _pruned;

    public string Server { get; } = Guid.NewGuid().ToString("N");

    public string? UnavailableKey => database.Value.Provider == DatabaseProvider.MySql ? NotOnMySqlKey : null;

    private bool Sqlite => database.Value.Provider == DatabaseProvider.Sqlite;

    public void Enqueue(Guid runId, Guid userId, string sessionToken)
    {
        _held[runId] = new Held(userId, sessionToken, sessions.Hold(userId, sessionToken));
        Wake();
    }

    public ImportRunProgress? ProgressOf(Guid runId) => _held.TryGetValue(runId, out var held) ? held.Progress : null;

    public string? SessionOf(Guid runId) =>
        _held.TryGetValue(runId, out var held) && sessions.Find(held.UserId, held.Token) is { State: BackupImportState.Opened } ? held.Token : null;

    public void Dropped(Guid runId)
    {
        if (_held.TryRemove(runId, out var held)) held.Hold?.Dispose();
    }

    public void Cancelling(Guid runId, Guid by)
    {
        if (_held.TryGetValue(runId, out var held)) held.CancelledBy ??= by;
    }

    public void Wake() => _wake.Release();

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "The import queue could not be looked at; trying again shortly");
            }
            try
            {
                await _wake.WaitAsync(Every, stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One look at the queue: who is gone, who waited too long, and whose turn it is. Never while a run of this server holds SQLite.</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        if (_running is { } running)
        {
            // On a database server the run's progress is written beside it; SQLite is locked by the run, and has one server.
            if (!Sqlite && _held.TryGetValue(running, out var held) && held.Progress is { } p) await runs.ProgressAsync(running, p.Car - 1, p.Done, ct);
            if (Sqlite) return;
        }
        var now = clock.GetUtcNow();
        await runs.SeenAsync(Server, now, ct);
        foreach (var run in await runs.ListActiveAsync(ct))
        {
            var mine = run.Server == Server;
            // A file is on the server that took it: a run of a server that is gone can never start (or, running, never end).
            var gone = !mine && (Sqlite || now - run.SeenAt > GoneAfter);
            if (run.State == ImportRunState.Waiting && now - run.QueuedAt > MaxWait) await EndAsync(run, ImportRunFailure.DidNotStart, null, ct);
            else if (run.State == ImportRunState.Waiting && (gone || (mine && !_held.ContainsKey(run.Id)))) await EndAsync(run, ImportRunFailure.Interrupted, null, ct);
            else if (run.State == ImportRunState.Running && run.Id != _running && (gone || mine) && await markers.FindAsync(run.Id, ct) is not { IsOpen: true })
                await EndAsync(run, ImportRunFailure.Interrupted, null, ct);
        }
        if (now - _pruned > TimeSpan.FromHours(1))
        {
            _pruned = now;
            var pruned = await runs.PruneAsync(now - KeepReports, ct);
            if (pruned > 0) logger.LogDebug("Removed {Count} import runs older than {Days} days", pruned, KeepReports.TotalDays);
        }
        if (_running is not null) return;
        // Whose turn: the oldest waiting run, when it is one of this server's. The claim is the database's word.
        var next = (await runs.ListActiveAsync(ct)).FirstOrDefault(r => r.State == ImportRunState.Waiting);
        if (next is null || next.Server != Server || !_held.ContainsKey(next.Id) || !await runs.ClaimAsync(next.Id, now, ct)) return;
        _running = next.Id;
        using (ExecutionContext.SuppressFlow()) _ = Task.Run(() => RunAsync(next.Id, ct), CancellationToken.None);
    }

    private async Task EndAsync(ImportRun run, ImportRunFailure failure, Guid? cancelledBy, CancellationToken ct)
    {
        if (!await runs.FailAsync(run.Id, failure, cancelledBy, clock.GetUtcNow(), ct)) return;
        Dropped(run.Id);
        logger.LogWarning("Import run {RunId} of user {UserId} brought nothing in: {Failure}", run.Id, run.StartedById, failure);
        await using var services = scopes.CreateAsyncScope();
        await services.ServiceProvider.GetRequiredService<ImportRunNotifier>().NotFinishedAsync(run, failure, cancelledBy, ct);
    }

    private async Task RunAsync(Guid runId, CancellationToken stopping)
    {
        ImportRunFailure? failure = null;
        ImportRun? row = null;
        _held.TryGetValue(runId, out var held);
        try
        {
            await using var services = scopes.CreateAsyncScope();
            var sp = services.ServiceProvider;
            row = await runs.FindAsync(runId, stopping) ?? throw new InvalidOperationException("The run's row is gone.");
            var plan = ImportRunJson.Read<ImportRunPlan>(row.Plan) ?? throw new InvalidOperationException("The run's plan cannot be read.");
            // Whoever started it, as they are now: a starter disabled or deleted while the run waited imports nothing.
            Principal starter;
            if (auth.Value.Mode == AuthMode.None) starter = Principal.Anonymous;
            else if (await sp.GetRequiredService<IUserRepository>().FindByIdAsync(row.StartedById, stopping) is { IsDisabled: false } user)
                starter = new Principal(user.Id, user.DisplayName, user.Email, user.IsAdmin, user.AvatarImageId, user.PasswordChangeRequired);
            else throw new InvalidOperationException("The run's starter is gone.");
            var file = (held is null ? null : sessions.Cars(held.UserId, held.Token)) ?? throw new InvalidOperationException("The run's file is gone.");

            ImportRunReport? report = null;
            var targets = plan.Cars.Where(c => c.TargetVehicleId is not null).Select(c => c.TargetVehicleId!.Value).ToList();
            await scope.RunAsync(new RunStart(starter, targets, runId), async run =>
            {
                // A cancel that came after the row said Running but before the run's mark existed found nothing to mark: kept here, and
                // honoured before anything is saved.
                if (held?.CancelledBy is not null) throw new RunStoppedException(RunEnd.Cancelled);
                report = await sp.GetRequiredService<BackupImportWork>().RunAsync(run, starter, file, plan, p => held!.Progress = p, stopping);
                // The run's last save, inside its transaction: the report commits with the cars, or neither does.
                await runs.CompleteAsync(runId, ImportRunJson.Write(report), plan.Cars.Count, plan.Cars.Sum(c => c.Entries), clock.GetUtcNow(), stopping);
            }, stopping);

            // Committed. The file is done with; then who is told, and the log line (ids and counts only).
            Dropped(runId);
            sessions.End(held!.UserId, held.Token);
            logger.LogInformation("User {UserId} imported {Cars} cars from a backup in run {RunId}: {Refuelings} refuellings, {Expenses} expenses, {Schedules} recurring expenses; {Refused} entries could not be saved",
                row.StartedById, report!.Imported, runId, report.Cars.Sum(c => c.Refuelings), report.Cars.Sum(c => c.Expenses), report.Cars.Sum(c => c.Schedules), report.Refused);
            await sp.GetRequiredService<ImportRunNotifier>().FinishedAsync(row, report, CancellationToken.None);
        }
        catch (RunStoppedException e)
        {
            failure = e.Reason == RunEnd.TimedOut ? ImportRunFailure.TimedOut : ImportRunFailure.Cancelled;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            failure = ImportRunFailure.Interrupted;
        }
        catch (Exception e)
        {
            failure = ImportRunFailure.Failed;
            // The type only: a database error quotes values, and here they are someone's logs.
            logger.LogError("Import run {RunId} failed unexpectedly ({Failure}); nothing of it was kept", runId, e.GetType().Name);
            logger.LogDebug(e, "Import run {RunId}: the failure", runId);
        }
        finally
        {
            _running = null;
            gate.Forget();
        }
        if (failure is { } why && row is not null)
        {
            try
            {
                Guid? by = why == ImportRunFailure.Cancelled ? held?.CancelledBy ?? (await markers.FindAsync(runId, CancellationToken.None))?.CancelRequestedById : null;
                held?.Hold?.Dispose(); // the file stays open for its half hour: back to the review
                if (held is not null) held.Hold = null;
                if (await runs.FailAsync(runId, why, by, clock.GetUtcNow(), CancellationToken.None))
                {
                    logger.LogWarning("Import run {RunId} of user {UserId} brought nothing in: {Failure}", runId, row.StartedById, why);
                    await using var services = scopes.CreateAsyncScope();
                    await services.ServiceProvider.GetRequiredService<ImportRunNotifier>().NotFinishedAsync(row, why, by, CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Import run {RunId} ended as {Failure}, which could not be recorded", runId, why);
            }
        }
        Wake();
    }

    /// <summary>The longest a run may hold its transaction, for the text that says a run was stopped past it.</summary>
    public int LimitMinutes => runOptions.CurrentValue.LimitMinutes;
}
