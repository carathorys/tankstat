using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Photos;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Images;
using Tankstat.Domain.Users;

namespace Tankstat.Application.Images;

/// <summary>An image ready to be sent to the browser.</summary>
public sealed record ImageContent(Stream Content, string ContentType, long SizeBytes) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Profile pictures, vehicle pictures and the file side of log photos (see <see cref="Photos.LogPhotoService"/>). A user changes
/// their own picture; a vehicle's picture needs edit access to the vehicle. Pictures are visible to everyone who may see their
/// subject: any signed-in user for avatars (they show next to names), whoever can see the vehicle for vehicle pictures, whoever
/// may see the log for its photos.
/// </summary>
public sealed class ImageService(
    IImageStore store, IImageRepository images, IUserRepository users, IVehicleRepository vehicles, AccessService access, LogPhotoAccess logPhotos, TimeProvider clock)
{
    public async Task<Guid> SetAvatarAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var previous = user.AvatarImageId;
        var id = await StoreAsync(data, ImageFolders.Avatar(user.Id), ct);

        user.SetAvatar(id);
        await users.UpdateAsync(user, ct);
        await DeleteQuietlyAsync(previous, ct);
        return id;
    }

    public async Task RemoveAvatarAsync(CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        var previous = user.AvatarImageId;
        user.SetAvatar(null);
        await users.UpdateAsync(user, ct);
        await DeleteQuietlyAsync(previous, ct);
    }

    public async Task<Guid> SetVehiclePictureAsync(Guid vehicleId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var vehicle = await EditableVehicleAsync(vehicleId, ct);
        var previous = vehicle.PictureImageId;
        var id = await StoreAsync(data, ImageFolders.VehiclePicture(vehicle.Id), ct);

        vehicle.SetPicture(id);
        await vehicles.UpdateAsync(vehicle, ct);
        await DeleteQuietlyAsync(previous, ct);
        return id;
    }

    public async Task RemoveVehiclePictureAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await EditableVehicleAsync(vehicleId, ct);
        var previous = vehicle.PictureImageId;
        vehicle.SetPicture(null);
        await vehicles.UpdateAsync(vehicle, ct);
        await DeleteQuietlyAsync(previous, ct);
    }

    /// <summary>The picture if the current user may see it; null for unknown pictures and for ones they may not see.</summary>
    public async Task<ImageContent?> OpenAsync(Guid imageId, CancellationToken ct)
    {
        var principal = await access.RequirePrincipalAsync(ct);
        if (!await CanSeeAsync(imageId, principal, ct)) return null;

        var info = await images.FindAsync(imageId, ct);
        if (info is null || await store.OpenReadAsync(info, ct) is not { } stream) return null;
        return new ImageContent(stream, info.ContentType, info.SizeBytes);
    }

    /// <summary>Removes the pictures of things that were deleted for good (their files and rows).</summary>
    public async Task DeleteAsync(IEnumerable<Guid> imageIds, CancellationToken ct)
    {
        foreach (var id in imageIds) await DeleteQuietlyAsync(id, ct);
    }

    /// <summary>
    /// Removes everything uploaded for vehicles that were deleted for good: their whole folder (picture and log photos), plus
    /// <paramref name="legacyImageIds"/>, pictures stored before vehicles had folders.
    /// </summary>
    public async Task DeleteVehicleFilesAsync(IEnumerable<Guid> vehicleIds, IEnumerable<Guid> legacyImageIds, CancellationToken ct)
    {
        await DeleteAsync(legacyImageIds, ct);
        await DeleteFoldersAsync(vehicleIds.Select(ImageFolders.Vehicle), ct);
    }

    /// <summary>
    /// Removes folders with every file and row below them. Used after what owned them was already deleted, so a folder that cannot be
    /// removed (permissions, a locked file) is skipped instead of failing the whole operation and leaving the other folders behind.
    /// </summary>
    public async Task DeleteFoldersAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        foreach (var folder in folders)
        {
            await images.RemoveFolderAsync(folder, ct);
            try
            {
                await store.DeleteFolderAsync(folder, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // an orphaned folder is harmless; nothing can reach it any more
            }
        }
    }

    private async Task<bool> CanSeeAsync(Guid imageId, Principal principal, CancellationToken ct)
    {
        if (await users.FindByAvatarImageAsync(imageId, ct) is not null) return true;
        if (await vehicles.FindByPictureImageAsync(imageId, ct) is { } vehicle) return await access.VehicleLevelAsync(vehicle, ct) >= AccessLevel.View;
        return await logPhotos.CanSeeImageAsync(imageId, ct);
    }

    private async Task<User> CurrentUserAsync(CancellationToken ct)
    {
        var principal = await access.RequirePrincipalAsync(ct);
        if (principal.IsAnonymous) throw new DomainException("image.noAccount", "Profile pictures need a user account; authentication is turned off.");
        return await users.FindByIdAsync(principal.Id, ct) ?? throw new UnauthenticatedException();
    }

    private async Task<Domain.Vehicles.Vehicle> EditableVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var level = vehicle is null ? AccessLevel.None : await access.VehicleLevelAsync(vehicle, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId });
        if (level < AccessLevel.Edit) throw new ForbiddenException("vehicle.viewOnly", "You may only view this vehicle.");
        return vehicle;
    }

    /// <summary>Validates, then writes the file first and the row second; a failure in between removes the file again.</summary>
    public async Task<Guid> StoreAsync(ReadOnlyMemory<byte> data, string folder, CancellationToken ct)
    {
        var contentType = ImageFormat.Detect(data.Span);
        var image = StoredImage.Create(Guid.NewGuid(), contentType, data.Length, clock.GetUtcNow(), folder);

        await store.SaveAsync(image, data, ct);
        try
        {
            await images.AddAsync(image, ct);
        }
        catch
        {
            await store.DeleteAsync(image, ct);
            throw;
        }
        return image.Id;
    }

    private async Task DeleteQuietlyAsync(Guid? id, CancellationToken ct)
    {
        if (id is null || await images.FindAsync(id.Value, ct) is not { } image) return;
        await images.RemoveAsync(image.Id, ct);
        await store.DeleteAsync(image, ct);
    }
}
