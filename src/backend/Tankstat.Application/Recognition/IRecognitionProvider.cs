using Tankstat.Domain.Recognition;

namespace Tankstat.Application.Recognition;

/// <summary>
/// Port to a service that reads values from photos: a vision model behind an OpenAI-compatible API. Only the server talks to it, never the
/// browser. Infrastructure implements it and the <c>Recognition</c> settings turn it on.
/// </summary>
public interface IRecognitionProvider
{
    /// <summary>A short name kept with each reading (<c>openai-compatible</c>).</summary>
    string Name { get; }

    /// <summary>False when no provider is set up or its settings cannot be used: then nothing is queued or read at all.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Whether the provider answers right now (asked at most every 30 seconds, see <see cref="RecognitionAvailability"/>).
    /// </summary>
    /// <exception cref="RecognitionUnavailableException">
    /// It does not, and why (unreachable, too slow, answering with an error): the reason is for the log, which would otherwise only say that
    /// photos are waiting. Any exception counts as "not healthy"; <c>false</c> is for a provider that has nothing more to say.
    /// </exception>
    Task<bool> IsHealthyAsync(CancellationToken ct);

    /// <exception cref="RecognitionUnavailableException">It cannot read now (unreachable, busy, timed out): try again later.</exception>
    /// <exception cref="RecognitionRejectedException">It will never read this photo (not a picture it can read, too large).</exception>
    Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct);
}

/// <param name="Kinds">What the photo may show.</param>
/// <param name="Locale">How numbers and dates are written (<c>hu</c>, <c>en</c>, <c>de</c>).</param>
/// <param name="LastOdometer">The vehicle's latest reading: a lower number cannot be its odometer.</param>
/// <param name="Currency">For receipts that show none.</param>
/// <param name="ReadingId">The photo's id (the reading's and the picture's), so the provider's log lines can be told apart; never part of what the model is sent.</param>
public sealed record RecognitionRequest(
    ReadOnlyMemory<byte> Image, string ContentType, IReadOnlySet<DocumentKind> Kinds, string Locale, long? LastOdometer, string? Currency, DateOnly Today,
    Guid? ReadingId = null);

/// <param name="Values">As the provider wrote them; they are normalised (<see cref="ReadingNormaliser"/>) before they are kept.</param>
public sealed record RecognitionResult(string ModelVersion, DocumentKind Kind, IReadOnlyList<RecognizedValue> Values)
{
    /// <summary>Why values the photo may hold were not taken from it, or only with doubt (see <see cref="ReadingIssue"/>); for the log and the dialog.</summary>
    public IReadOnlyList<ReadingIssue> Issues { get; init; } = [];
}

public sealed record RecognizedValue(ReadingFieldName Name, string Value, double Confidence, ValueSource Source);

/// <summary>The provider cannot read now (unreachable, busy, timed out, refusing the key): the reading is tried again later.</summary>
public sealed class RecognitionUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The provider will never read this photo (not a picture it can read, too large): the reading fails without further attempts.</summary>
public sealed class RecognitionRejectedException(string message) : Exception(message);

/// <summary>Wakes the background worker when a reading was queued (otherwise it looks every few seconds on its own).</summary>
public interface IRecognitionSignal
{
    void Wake();
}
