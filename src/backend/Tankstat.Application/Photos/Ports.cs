using Tankstat.Domain.Photos;

namespace Tankstat.Application.Photos;

public interface ILogPhotoRepository
{
    /// <summary>The photos of a log, oldest first.</summary>
    Task<IReadOnlyList<LogPhoto>> ListForLogAsync(LogType logType, Guid logId, CancellationToken ct);
    Task<int> CountForLogAsync(LogType logType, Guid logId, CancellationToken ct);

    /// <summary>The photo that shows this image, if the image is a log photo.</summary>
    Task<LogPhoto?> FindByImageAsync(Guid imageId, CancellationToken ct);

    Task AddAsync(LogPhoto photo, CancellationToken ct);
    Task RemoveAsync(LogPhoto photo, CancellationToken ct);
}

/// <summary>The logs that were deleted for good: how many, and (only those that had photos) their vehicle and id, so their photo folders can be removed.</summary>
public sealed record PurgedLogs(int Count, IReadOnlyList<(Guid VehicleId, Guid LogId)> WithPhotos);
