using Microsoft.Extensions.Options;

namespace Tankstat.Reader.Reading;

/// <summary>
/// Lets a fixed number of photos be read at the same time and a few more wait; anything beyond is turned away at once as busy, so a
/// burst of uploads neither starves the machine nor piles up requests that time out anyway.
/// </summary>
internal sealed class ReadGate(IOptions<ReaderOptions> options)
{
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrent, options.Value.MaxConcurrent);
    private int _waiting;

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (!_slots.Wait(0))
        {
            if (Interlocked.Increment(ref _waiting) > options.Value.QueueLimit)
            {
                Interlocked.Decrement(ref _waiting);
                throw ReaderErrors.Busy();
            }
            try
            {
                await _slots.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }
        try
        {
            return await work(ct);
        }
        finally
        {
            _slots.Release();
        }
    }
}
