using Tankstat.Domain.Images;

namespace Tankstat.Application.Images;

/// <summary>Where the picture bytes are kept (the data folder on disk).</summary>
public interface IImageStore
{
    Task SaveAsync(Guid id, ReadOnlyMemory<byte> data, CancellationToken ct);
    Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>The metadata rows of the pictures.</summary>
public interface IImageRepository
{
    Task AddAsync(StoredImage image, CancellationToken ct);
    Task<StoredImage?> FindAsync(Guid id, CancellationToken ct);
    Task RemoveAsync(Guid id, CancellationToken ct);
}
