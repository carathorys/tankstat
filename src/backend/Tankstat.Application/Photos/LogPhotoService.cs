using Microsoft.Extensions.Logging;
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
    LogPhotoAccess logs, ILogPhotoRepository photos, ImageService images, PhotoDraftService drafts, AccessService access, TimeProvider clock,
    ILogger<LogPhotoService> logger)
{
    /// <summary>The photos of a log, oldest first; empty when the log does not exist or may not be seen.</summary>
    public async Task<IReadOnlyList<LogPhoto>> ListAsync(LogType logType, Guid logId, CancellationToken ct) =>
        await logs.FindAsync(logType, logId, ct) is null ? [] : await photos.ListForLogAsync(logType, logId, ct);

    /// <summary>
    /// The photos of several logs the caller already got through the log services (and so may see) in one query, oldest first per log.
    /// Logs in the trash have no visible photos, like <see cref="ListAsync"/>. This does not check access again: never pass ids that
    /// did not come from an authorised log.
    /// </summary>
    public async Task<ILookup<Guid, LogPhoto>> ListForLogsAsync(LogType logType, IReadOnlyCollection<Guid> logIds, CancellationToken ct) =>
        logIds.Count == 0 ? Enumerable.Empty<LogPhoto>().ToLookup(p => p.LogId) : (await photos.ListForLogsAsync(logType, logIds, ct)).ToLookup(p => p.LogId);

    // Uploads to one log are handled one at a time within this process, so the count check and the insert below are one step. Other
    // processes on the same database are covered by the re-check after the insert. Striped (by log id) so the locks do not pile up.
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>Stores the picture (JPEG, PNG or WebP) as a new photo of the log and returns its image id.</summary>
    public async Task<Guid> AddAsync(LogType logType, Guid logId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var log = await logs.EditableAsync(logType, logId, ct);
        var principal = await access.RequirePrincipalAsync(ct);

        var gate = Locks[(uint)logId.GetHashCode() % Locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (await photos.CountForLogAsync(logType, logId, ct) >= LogPhoto.MaxPerLog) throw TooMany();

            var imageId = await images.StoreAsync(data, ImageFolders.LogPhotos(log.Vehicle.Id, logType, logId), ct);
            LogPhoto? inserted = null;
            try
            {
                var photo = LogPhoto.Create(log.Vehicle.OwnerId, log.Vehicle.Id, logType, logId, imageId, principal.Id, clock.GetUtcNow());
                await photos.AddAsync(photo, ct);
                inserted = photo;

                // Uploads through another process can still pass the count above at the same moment. Look again: the first MaxPerLog
                // photos (in the order they are listed) stay and every one after them is taken back, whichever request notices.
                var extra = (await photos.ListForLogAsync(logType, logId, ct)).Skip(LogPhoto.MaxPerLog).ToList();
                if (extra.Count > 0) logger.LogWarning("{Count} photos over the limit of {LogType} {LogId} were taken back after concurrent uploads", extra.Count, logType, logId);
                foreach (var surplus in extra)
                {
                    await photos.RemoveAsync(surplus, ct);
                    if (surplus.ImageId == imageId) inserted = null;
                    else await images.DeleteAsync([surplus.ImageId], ct);
                }
                if (inserted is null) throw TooMany();
            }
            catch
            {
                // Best effort and not tied to the request: a client that went away must not leave the row or the file behind.
                await CleanUpAsync(inserted, imageId);
                throw;
            }
            logger.LogDebug("User {UserId} added photo {ImageId} to {LogType} {LogId}", principal.Id, imageId, logType, logId);
            return imageId;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The drafts a new log of the vehicle may take (see <see cref="PhotoDraftService.RequireAttachableAsync"/>); call it before saving
    /// the log, then <see cref="AttachDraftsAsync"/> after.
    /// </summary>
    public Task<IReadOnlyList<PhotoDraft>> RequireDraftsAsync(Guid vehicleId, IReadOnlyCollection<Guid>? draftIds, CancellationToken ct) =>
        drafts.RequireAttachableAsync(vehicleId, draftIds, ct);

    /// <summary>
    /// Makes the drafts photos of the log that was just saved: each picture moves into the log's folder and gets its photo row. The log
    /// is new and the drafts were checked, so they fit; a draft that fails here stays a draft (and expires) rather than failing the save.
    /// </summary>
    public async Task AttachDraftsAsync(LogType logType, Guid logId, IReadOnlyList<PhotoDraft> attachable, CancellationToken ct)
    {
        if (attachable.Count == 0) return;
        var attached = new List<Guid>();
        var now = clock.GetUtcNow();
        foreach (var draft in attachable)
        {
            var moved = false;
            try
            {
                await images.MoveAsync(draft.Id, ImageFolders.LogPhotos(draft.VehicleId, logType, logId), CancellationToken.None);
                moved = true;
                await photos.AddAsync(LogPhoto.Create(draft.OwnerId, draft.VehicleId, logType, logId, draft.Id, draft.CreatedById, now), CancellationToken.None);
                attached.Add(draft.Id);
            }
            catch (Exception e)
            {
                // The log is saved already: failing now would make the user save it twice. The picture goes back to the drafts.
                logger.LogWarning(e, "Draft photo {DraftId} could not be attached to {LogType} {LogId}; it stays a draft", draft.Id, logType, logId);
                if (moved) await MoveBackQuietlyAsync(draft);
            }
        }
        await drafts.ForgetAsync(attached, CancellationToken.None);
        logger.LogDebug("Attached {Attached} of {Wanted} draft photos to {LogType} {LogId}", attached.Count, attachable.Count, logType, logId);
    }

    private async Task MoveBackQuietlyAsync(PhotoDraft draft)
    {
        try
        {
            await images.MoveAsync(draft.Id, ImageFolders.PhotoDrafts(draft.VehicleId), CancellationToken.None);
        }
        catch (Exception e)
        {
            // it expires with the other drafts or goes with the vehicle
            logger.LogWarning(e, "Draft photo {DraftId} could not be moved back to the drafts", draft.Id);
        }
    }

    private async Task CleanUpAsync(LogPhoto? row, Guid imageId)
    {
        try
        {
            if (row is not null) await photos.RemoveAsync(row, CancellationToken.None);
            await images.DeleteAsync([imageId], CancellationToken.None);
        }
        catch (Exception e)
        {
            // the original error is the one to report; this one only says what is left behind
            logger.LogWarning(e, "Photo {ImageId} could not be cleaned up after a failed upload", imageId);
        }
    }

    private static DomainException TooMany() =>
        new("photo.tooMany", $"A log can have at most {LogPhoto.MaxPerLog} photos.", new { Max = LogPhoto.MaxPerLog });

    public async Task RemoveAsync(LogType logType, Guid logId, Guid imageId, CancellationToken ct)
    {
        await logs.EditableAsync(logType, logId, ct);
        var photo = await photos.FindByImageAsync(imageId, ct);
        if (photo is null || photo.LogType != logType || photo.LogId != logId)
            throw new NotFoundException("photo.notFound", $"The photo {imageId} does not exist.", new { Id = imageId });

        await photos.RemoveAsync(photo, ct);
        await images.DeleteAsync([imageId], ct);
        logger.LogDebug("Photo {ImageId} removed from {LogType} {LogId}", imageId, logType, logId);
    }

    /// <summary>Removes the files of the photos of logs that were deleted for good (their rows went with the logs).</summary>
    public Task DeleteFilesAsync(LogType logType, PurgedLogs purged, CancellationToken ct) =>
        images.DeleteFoldersAsync(purged.WithPhotos.Select(l => ImageFolders.LogPhotos(l.VehicleId, logType, l.LogId)), ct);
}
