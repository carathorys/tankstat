using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Users;

namespace Tankstat.Infrastructure.Auth;

/// <summary>
/// The self-service reset requests waiting for <see cref="PasswordResetMailer"/>, in memory: a request still waiting when the app stops is
/// lost, and the user asks again. A user is in it at most once (a later request of theirs is served by the waiting one), so the backlog is
/// bounded by accounts, not by requests; once the mailer takes a request, a new one of that user is queued again and the cool-down decides.
/// </summary>
internal sealed class PasswordResetQueue : IPasswordResetQueue
{
    public const int DefaultCapacity = 1000;

    private readonly Channel<Guid> _channel;
    private readonly HashSet<Guid> _waiting = [];
    private readonly Lock _gate = new();

    // Requests queued or being served, and a task that completes when there are none (for tests: no sleeping until the mail went out).
    private int _pending;
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PasswordResetQueue(int capacity)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _idle.SetResult();
    }

    public ResetRequestQueueing Enqueue(Guid userId)
    {
        lock (_gate)
        {
            if (_waiting.Contains(userId)) return ResetRequestQueueing.AlreadyWaiting;
            if (!_channel.Writer.TryWrite(userId)) return ResetRequestQueueing.QueueFull;
            _waiting.Add(userId);
            if (_pending++ == 0) _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return ResetRequestQueueing.Queued;
        }
    }

    /// <summary>The next request; from now on a new request of that user is queued again.</summary>
    internal async ValueTask<Guid> TakeAsync(CancellationToken ct)
    {
        var userId = await _channel.Reader.ReadAsync(ct);
        lock (_gate) _waiting.Remove(userId);
        return userId;
    }

    /// <summary>A request taken by <see cref="TakeAsync"/> was served (or failed).</summary>
    internal void Done()
    {
        lock (_gate)
        {
            if (--_pending == 0) _idle.TrySetResult();
        }
    }

    /// <summary>Completes once every request queued so far was served (at once when none waits).</summary>
    internal Task WhenIdleAsync()
    {
        lock (_gate) return _idle.Task;
    }
}

/// <summary>
/// Sends the e-mails of self-service reset requests after they were answered, one at a time, through
/// <see cref="PasswordResetService.SendRequestedAsync"/> in a scope of its own. A request that fails unexpectedly is an Error, and the next
/// one is still served; a mail server's failure is the service's Warning. Says nothing at start.
/// </summary>
internal sealed class PasswordResetMailer(IServiceScopeFactory scopes, PasswordResetQueue queue, ILogger<PasswordResetMailer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (true)
        {
            Guid userId;
            try
            {
                userId = await queue.TakeAsync(stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetService>().SendRequestedAsync(userId, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return; // stopping: the link of a send cut short was taken back
            }
            catch (Exception e)
            {
                logger.LogError(e, "Sending the password reset requested by user {UserId} failed", userId);
            }
            finally
            {
                queue.Done();
            }
        }
    }
}
