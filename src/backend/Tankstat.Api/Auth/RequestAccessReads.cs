using Tankstat.Application.Access;

namespace Tankstat.Api.Auth;

/// <summary>
/// What <see cref="AccessService"/> read of the access tables, kept with the request (#320) in its <see cref="HttpContext.Items"/>, beside the
/// principal (<see cref="HttpCurrentUser"/>): HotChocolate gives every query field and data loader a service scope of its own, so only the
/// request is shared by all of them. Fields and loaders resolve in parallel: the first to ask starts the read and the others await the same
/// one. Without a request (a background worker, a command) every ask reads.
/// </summary>
internal sealed class RequestAccessReads(IHttpContextAccessor accessor) : IAccessReads
{
    /// <summary>The key of the request's values in <see cref="HttpContext.Items"/>.</summary>
    private static readonly object ReadsKey = new();

    public Task<T> GetAsync<T>(AccessReadKey key, Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        if (accessor.HttpContext is not { } context) return read(ct);
        // The read itself runs outside the lock: SQLite's "async" queries run synchronously, which would hold every other field on it.
        TaskCompletionSource<T> started;
        lock (context.Items)
        {
            if (context.Items[ReadsKey] is not Dictionary<AccessReadKey, object> kept) context.Items[ReadsKey] = kept = [];
            if (kept.TryGetValue(key, out var shared)) return (Task<T>)shared;
            started = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            kept[key] = started.Task;
        }
        _ = RunAsync(started, read, ct);
        return started.Task;
    }

    public void Clear()
    {
        if (accessor.HttpContext is not { } context) return;
        lock (context.Items) context.Items.Remove(ReadsKey);
    }

    private static async Task RunAsync<T>(TaskCompletionSource<T> started, Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        try
        {
            started.SetResult(await read(ct));
        }
        catch (OperationCanceledException e)
        {
            started.SetCanceled(e.CancellationToken);
        }
        catch (Exception e)
        {
            started.SetException(e);
        }
    }
}
