using Tankstat.Application.Access;
using Tankstat.Application.Images;
using Tankstat.Domain;
using Tankstat.Domain.Images;
using Tankstat.Domain.Photos;

namespace Tankstat.Application.Photos;

/// <summary>
/// Photos of refuelings and expenses (receipts and the like), up to <see cref="LogPhoto.MaxPerLog"/> per log. Seeing them needs View on
/// the vehicle's logs, adding and removing needs Edit; they are stored in the vehicle's upload folder and go away with their log.
/// </summary>
public sealed class LogPhotoService(
    LogPhotoAccess logs, ILogPhotoRepository photos, ImageService images, AccessService access, TimeProvider clock)
{
    /// <summary>The photos of a log, oldest first; empty when the log does not exist or may not be seen.</summary>
    public async Task<IReadOnlyList<LogPhoto>> ListAsync(LogType logType, Guid logId, CancellationToken ct) =>
        await logs.FindAsync(logType, logId, ct) is null ? [] : await photos.ListForLogAsync(logType, logId, ct);

    /// <summary>Stores the picture (JPEG, PNG or WebP) as a new photo of the log and returns its image id.</summary>
    public async Task<Guid> AddAsync(LogType logType, Guid logId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var log = await logs.EditableAsync(logType, logId, ct);
        var principal = await access.RequirePrincipalAsync(ct);
        if (await photos.CountForLogAsync(logType, logId, ct) >= LogPhoto.MaxPerLog)
            throw new DomainException("photo.tooMany", $"A log can have at most {LogPhoto.MaxPerLog} photos.", new { Max = LogPhoto.MaxPerLog });

        var imageId = await images.StoreAsync(data, ImageFolders.LogPhotos(log.Vehicle.Id, logType, logId), ct);
        try
        {
            await photos.AddAsync(LogPhoto.Create(log.Vehicle.OwnerId, log.Vehicle.Id, logType, logId, imageId, principal.Id, clock.GetUtcNow()), ct);
        }
        catch
        {
            await images.DeleteAsync([imageId], ct);
            throw;
        }
        return imageId;
    }

    public async Task RemoveAsync(LogType logType, Guid logId, Guid imageId, CancellationToken ct)
    {
        await logs.EditableAsync(logType, logId, ct);
        var photo = await photos.FindByImageAsync(imageId, ct);
        if (photo is null || photo.LogType != logType || photo.LogId != logId)
            throw new NotFoundException("photo.notFound", $"The photo {imageId} does not exist.", new { Id = imageId });

        await photos.RemoveAsync(photo, ct);
        await images.DeleteAsync([imageId], ct);
    }

    /// <summary>Removes the files of the photos of logs that were deleted for good (their rows went with the logs).</summary>
    public Task DeleteFilesAsync(LogType logType, PurgedLogs purged, CancellationToken ct) =>
        images.DeleteFoldersAsync(purged.Logs.Select(l => ImageFolders.LogPhotos(l.VehicleId, logType, l.LogId)), ct);
}
