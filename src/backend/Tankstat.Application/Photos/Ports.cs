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

/// <summary>
/// What came of deleting every log of one kind of one car for good (#338, an import's <i>Replace all</i>): how many went, or, when any of
/// them has photos or belongs to a tyre log line, how many of each, and then nothing was deleted (a backup cannot bring those back yet).
/// </summary>
/// <param name="Photos">The images of the photos deleted with the logs (#339: photos no longer hold a purge back): their files and rows go
/// through the image service, so inside a run the files wait for the commit.</param>
public sealed record VehiclePurge(int Deleted, int WithPhotos, int OnTyreLines, IReadOnlyList<Guid>? Photos = null)
{
    public bool HeldBack => WithPhotos + OnTyreLines > 0;
}

/// <summary>Why a car's logs of one kind are not deleted for good (#338).</summary>
public static class VehiclePurgeRefusals
{
    public const string HeldBackKey = "import.replaceHeldBack";
    public const string NoDeleteKey = "vehicle.deleteNotAllowed";

    public static Tankstat.Domain.DomainException HeldBack(VehiclePurge purge) =>
        new(HeldBackKey, "These entries have photos or tyre log lines, so they cannot be replaced yet.", new { Photos = purge.WithPhotos, Lines = purge.OnTyreLines });
}
