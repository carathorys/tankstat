using Tankstat.Application.Recognition;

namespace Tankstat.Infrastructure.Recognition;

/// <summary>No provider is set up (or its settings cannot be used): photos are not read, and nothing ever asks it to.</summary>
internal sealed class NullRecognitionProvider : IRecognitionProvider
{
    public static readonly NullRecognitionProvider Instance = new();

    public string Name => "none";
    public bool IsConfigured => false;
    public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(false);
    public Task<RecognitionResult> ReadAsync(RecognitionRequest request, CancellationToken ct) =>
        throw new RecognitionUnavailableException("Photo reading is not set up.");
}
