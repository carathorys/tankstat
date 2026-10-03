namespace Tankstat.Application.Recognition;

/// <summary>
/// Whether photos can be read right now: a provider is set up and answers. The answer is kept for <see cref="CacheFor"/>, so the UI and
/// the worker can ask as often as they like without bothering the provider.
/// </summary>
public sealed class RecognitionAvailability(IRecognitionProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private sealed record Answer(bool Healthy, DateTimeOffset At);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Answer? _last;

    /// <summary>A provider is set up (whether or not it answers right now).</summary>
    public bool IsConfigured => provider.IsConfigured;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (!provider.IsConfigured) return false;
        if (Fresh() is { } known) return known.Healthy;
        await _gate.WaitAsync(ct);
        try
        {
            if (Fresh() is { } meanwhile) return meanwhile.Healthy;
            bool healthy;
            try
            {
                healthy = await provider.IsHealthyAsync(ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                healthy = false;
            }
            _last = new Answer(healthy, clock.GetUtcNow());
            return healthy;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A reading just found the provider unavailable: the next question asks it again instead of trusting the kept answer.</summary>
    public void Forget() => _last = null;

    private Answer? Fresh() => _last is { } last && clock.GetUtcNow() - last.At < CacheFor ? last : null;
}
