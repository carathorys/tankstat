using Tankstat.Domain.Photos;

namespace Tankstat.Application.Photos;

public interface ILogPhotoRepository
{
    /// <summary>The photos of a log, oldest first.</summary>
    Task<IReadOnlyList<LogPhoto>> ListForLogAsync(LogType logType, Guid logId, CancellationToken ct);
    /// <summary>The photos of several logs of one kind in one query, each log's oldest first.</summary>
    Task<IReadOnlyList<LogPhoto>> ListForLogsAsync(LogType logType, IReadOnlyCollection<Guid> logIds, CancellationToken ct);
    Task<int> CountForLogAsync(LogType logType, Guid logId, CancellationToken ct);

    /// <summary>The photo that shows this image, if the image is a log photo.</summary>
    Task<LogPhoto?> FindByImageAsync(Guid imageId, CancellationToken ct);

    Task AddAsync(LogPhoto photo, CancellationToken ct);
    Task RemoveAsync(LogPhoto photo, CancellationToken ct);
}

public interface IPhotoDraftRepository
{
    Task<PhotoDraft?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PhotoDraft>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<int> CountAsync(Guid vehicleId, Guid createdById, CancellationToken ct);

    /// <summary>Every draft (anyone's, on any vehicle) created before <paramref name="before"/>.</summary>
    Task<IReadOnlyList<PhotoDraft>> ListCreatedBeforeAsync(DateTimeOffset before, CancellationToken ct);

    Task AddAsync(PhotoDraft draft, CancellationToken ct);
    Task RemoveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

/// <summary>The logs that were deleted for good: how many, and (only those that had photos) their vehicle and id, so their photo folders can be removed.</summary>
public sealed record PurgedLogs(int Count, IReadOnlyList<(Guid VehicleId, Guid LogId)> WithPhotos);
