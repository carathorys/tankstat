using Tankstat.Domain.Images;

namespace Tankstat.Application.Images;

/// <summary>Where the picture bytes are kept (the data folder on disk); each file sits in the folder its <see cref="StoredImage"/> names.</summary>
public interface IImageStore
{
    Task SaveAsync(StoredImage image, ReadOnlyMemory<byte> data, CancellationToken ct);
    Task<Stream?> OpenReadAsync(StoredImage image, CancellationToken ct);
    Task DeleteAsync(StoredImage image, CancellationToken ct);

    /// <summary>Removes a folder with everything below it (a purged vehicle's files); nothing happens if it does not exist.</summary>
    Task DeleteFolderAsync(string folder, CancellationToken ct);
}

/// <summary>The metadata rows of the pictures.</summary>
public interface IImageRepository
{
    Task AddAsync(StoredImage image, CancellationToken ct);
    Task<StoredImage?> FindAsync(Guid id, CancellationToken ct);
    Task RemoveAsync(Guid id, CancellationToken ct);

    /// <summary>Removes the rows of every image in the folder or below it.</summary>
    Task RemoveFolderAsync(string folder, CancellationToken ct);
}
