using Tankstat.Application.Images;
using Tankstat.Domain.Recognition;

namespace Tankstat.Application.Recognition;

public enum ReadingOutcome
{
    Read,
    /// <summary>The provider could not read it now; it is queued again for later.</summary>
    Retrying,
    Failed,
}

/// <summary>What happened to one reading in a round of the worker (for its log; never the values themselves).</summary>
public sealed record ProcessedReading(Guid Id, ReadingOutcome Outcome, string? Reason = null);

/// <summary>
/// The background worker's job: read the photos that are due, a few at a time, through the recognition provider, and keep what was
/// found. Nothing is claimed while the provider is unavailable, so a stopped reader does not use up the attempts.
/// </summary>
public sealed class PhotoReadingProcessor(
    IRecognitionProvider provider, RecognitionAvailability availability, RecognitionSetup setup, IPhotoReadingRepository readings,
    IImageRepository images, IImageStore store, TimeProvider clock)
{
    /// <summary>An attempt this old was cut short (the app stopped while reading): it is queued again.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Reads the due photos, up to <see cref="RecognitionOptions.MaxConcurrent"/> at a time; empty when nothing could be done.</summary>
    public async Task<IReadOnlyList<ProcessedReading>> ProcessDueAsync(CancellationToken ct)
    {
        if (!await availability.IsAvailableAsync(ct)) return [];

        var now = clock.GetUtcNow();
        foreach (var stale in await readings.ListStaleAsync(now - StaleAfter, ct))
        {
            stale.Abandon(now);
            await readings.SaveAsync(stale, ct);
        }

        var due = await readings.ListDueAsync(now, setup.Options.MaxConcurrent, ct);
        var done = await Task.WhenAll(due.Select(id => ProcessAsync(id, ct)));
        return done.OfType<ProcessedReading>().ToList();
    }

    private async Task<ProcessedReading?> ProcessAsync(Guid id, CancellationToken ct)
    {
        if (await readings.ClaimAsync(id, clock.GetUtcNow(), ct) is not { } reading) return null; // taken by another worker, or gone
        if (await images.FindAsync(id, ct) is not { } image) return null; // the picture went meanwhile, and its reading with it

        ProcessedReading outcome;
        var data = await LoadAsync(image, ct);
        if (data is null)
        {
            reading.Fail();
            outcome = new ProcessedReading(id, ReadingOutcome.Failed, "The picture's file is missing.");
        }
        else
        {
            try
            {
                var result = await provider.ReadAsync(
                    new RecognitionRequest(data, image.ContentType, reading.AllowedKinds, reading.Locale, reading.LastOdometer, reading.Currency, reading.Today), ct);
                reading.Complete(provider.Name, result.ModelVersion, result.Kind, ReadingNormaliser.Normalise(result.Values), clock.GetUtcNow());
                outcome = new ProcessedReading(id, ReadingOutcome.Read);
            }
            catch (RecognitionRejectedException e)
            {
                reading.Fail();
                outcome = new ProcessedReading(id, ReadingOutcome.Failed, e.Message);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Unavailable (or an unexpected answer): later, with the next attempt. Asking the provider again then confirms it is back.
                availability.Forget();
                reading.Retry(clock.GetUtcNow());
                outcome = new ProcessedReading(id, reading.Status == ReadingStatus.Failed ? ReadingOutcome.Failed : ReadingOutcome.Retrying, e.Message);
            }
        }
        await readings.SaveAsync(reading, ct);
        return outcome;
    }

    private async Task<byte[]?> LoadAsync(Domain.Images.StoredImage image, CancellationToken ct)
    {
        await using var stream = await store.OpenReadAsync(image, ct);
        if (stream is null) return null;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        return copy.ToArray();
    }
}
