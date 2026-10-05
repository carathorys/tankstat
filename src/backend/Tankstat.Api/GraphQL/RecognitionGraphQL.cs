using Tankstat.Api.Media;
using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;

namespace Tankstat.Api.GraphQL;

/// <summary>Whether photos picked in the add dialogs are read on the server (a provider is set up and answers).</summary>
public sealed record RecognitionStatusInfo(bool Available);

/// <summary>A value read from a photo: invariant text (<c>38.52</c>, <c>2026-09-17</c>, <c>HUF</c>) and how sure the provider was (0 to 1).</summary>
public sealed record ReadingValueInfo(ReadingFieldName Name, string Value, double Confidence);

/// <summary>What became of reading a photo; <c>values</c> holds only what is worth filling in.</summary>
public sealed record PhotoReadingInfo(ReadingStatus Status, DocumentKind? Kind, IReadOnlyList<ReadingValueInfo> Values);

/// <summary>A photo uploaded for a log that is not saved yet, with its reading (none when photo reading is off).</summary>
public sealed record PhotoDraftInfo(Guid Id, string Url, PhotoReadingInfo? Reading);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class RecognitionQueries
{
    /// <summary>Whether photos are read on the server right now; the add dialogs wait for readings only then.</summary>
    public async Task<RecognitionStatusInfo> GetRecognitionStatus([Service] RecognitionService recognition, CancellationToken ct) =>
        new(await recognition.IsAvailableAsync(ct));

    /// <summary>The caller's own draft photos among <c>ids</c> (in that order) with what was read from them; others' are left out.</summary>
    public async Task<IReadOnlyList<PhotoDraftInfo>> GetPhotoDrafts(IReadOnlyList<Guid> ids, [Service] RecognitionService recognition, CancellationToken ct) =>
        (await recognition.ListDraftsAsync(ids, ct))
            .Select(d => new PhotoDraftInfo(d.Draft.Id, MediaUrls.Image(d.Draft.Id)!, d.Reading is { } reading
                ? new PhotoReadingInfo(reading.Status, reading.Kind, recognition.UsableValues(reading).Select(v => new ReadingValueInfo(v.Name, v.Value, v.Confidence)).ToList())
                : null))
            .ToList();
}
