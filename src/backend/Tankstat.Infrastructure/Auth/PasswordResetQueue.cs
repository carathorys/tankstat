using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Users;

namespace Tankstat.Infrastructure.Auth;

/// <summary>
/// The self-service reset requests waiting for <see cref="PasswordResetMailer"/>, in memory: a request still waiting when the app stops is
/// lost, and the user asks again. A user is in it at most once (a later request of theirs is served by the waiting one, with the later
/// request's session version), so the backlog is bounded by accounts, not by requests; once the mailer takes a request, a new one of that
/// user is queued again and the cool-down decides.
/// </summary>
internal sealed class PasswordResetQueue : IPasswordResetQueue
{
    public const int DefaultCapacity = 1000;

    private readonly Channel<Guid> _channel;

    // The users waiting, with the session version of their latest request. A user is in it from before their id is written until the
    // mailer takes the id, both under _gate, so the mailer always finds the version of the id it read.
    private readonly Dictionary<Guid, int> _waiting = [];
    private readonly Lock _gate = new();

    // Requests queued or being served, and a task that completes when there are none (for tests: no sleeping until the mail went out).
    private int _pending;
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PasswordResetQueue(int capacity)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity) { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });
        _idle.SetResult();
    }

    public ResetRequestQueueing Enqueue(Guid userId, int sessionVersion)
    {
        lock (_gate)
        {
            if (_waiting.ContainsKey(userId))
            {
                _waiting[userId] = sessionVersion; // the waiting request serves this one, as the account stands now
                return ResetRequestQueueing.AlreadyWaiting;
            }
            _waiting.Add(userId, sessionVersion);
            if (!_channel.Writer.TryWrite(userId))
            {
                _waiting.Remove(userId);
                return ResetRequestQueueing.QueueFull;
            }
            if (_pending++ == 0) _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return ResetRequestQueueing.Queued;
        }
    }

    /// <summary>The next request, with the session version of the user's latest request; from now on a new request of that user is queued again.</summary>
    internal async ValueTask<(Guid UserId, int SessionVersion)> TakeAsync(CancellationToken ct) => Taken(await _channel.Reader.ReadAsync(ct));

    /// <summary>Like <see cref="TakeAsync"/>, but only a request already waiting (at stop, to say whose were lost).</summary>
    internal bool TryTake(out (Guid UserId, int SessionVersion) request)
    {
        var any = _channel.Reader.TryRead(out var userId);
        request = any ? Taken(userId) : default;
        return any;
    }

    private (Guid UserId, int SessionVersion) Taken(Guid userId)
    {
        lock (_gate)
        {
            _waiting.Remove(userId, out var sessionVersion);
            return (userId, sessionVersion);
        }
    }

    /// <summary>A request taken by <see cref="TakeAsync"/> or <see cref="TryTake"/> was served (or failed, or was dropped at stop).</summary>
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
/// Sends the e-mails of self-service reset requests after they were answered, <see cref="Senders"/> at a time, each through
/// <see cref="PasswordResetService.SendRequestedAsync"/> in a scope of its own. A request that fails unexpectedly is an Error, and the next
/// one is still served; a mail server's failure is the service's Warning. Says nothing at start. At stop, the requests it is serving and the
/// ones still waiting are lost (the users ask again), each an Information line by user id, so a mail that never came can be explained.
/// </summary>
internal sealed class PasswordResetMailer(IServiceScopeFactory scopes, PasswordResetQueue queue, ILogger<PasswordResetMailer> logger) : BackgroundService
{
    /// <summary>
    /// How many requests are served at once. Several, so a mail server that does not answer holds one sender for up to
    /// <see cref="SmtpEmailSender.SendTimeout"/> instead of the whole line, and one user's send seldom delays another's (which an account
    /// holder timing their own mail could read as "this other address is registered"); few, so a burst of requests for many accounts does
    /// not open a connection to the mail server each. Two requests of one user served at once (one taken, then asked again) are settled by
    /// <see cref="PasswordResetService"/>'s per-user lock and the cool-down, as a request and an administrator's issue are.
    /// </summary>
    internal const int Senders = 4;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await Task.WhenAll(Enumerable.Range(0, Senders).Select(_ => Task.Run(() => SendAsync(stopping), CancellationToken.None)));

        while (queue.TryTake(out var left))
        {
            logger.LogInformation("The password reset requested by user {UserId} was not sent: the app stopped before its turn", left.UserId);
            queue.Done();
        }
    }

    private async Task SendAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            Guid userId;
            int sessionVersion;
            try
            {
                (userId, sessionVersion) = await queue.TakeAsync(stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetService>().SendRequestedAsync(userId, sessionVersion, stopping);
            }
            catch (Exception) when (stopping.IsCancellationRequested)
            {
                // Stopping, whatever the database or the mail server made of the cancellation (not every provider reports it as an
                // OperationCanceledException): a link already stored for it was taken back.
                logger.LogInformation("The password reset requested by user {UserId} was not sent: the app is stopping", userId);
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
