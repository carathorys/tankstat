using Microsoft.Extensions.Options;
using Tankstat.Application.Images;

namespace Tankstat.Infrastructure.Storage;

/// <summary>
/// Keeps picture bytes as plain files named after the image id (a GUID, so nothing user-controlled ever reaches a path) in the
/// data folder (<c>Storage:Path</c>, in Docker the /data volume).
/// </summary>
internal sealed class FileSystemImageStore(IOptions<StorageOptions> options) : IImageStore
{
    private string PathFor(Guid id) => Path.Combine(Path.GetFullPath(options.Value.Path), id.ToString("N"));

    public async Task SaveAsync(Guid id, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var target = PathFor(id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Write beside the target and move into place, so a reader never sees a half-written picture.
        var temp = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, data.ToArray(), ct);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct)
    {
        var path = PathFor(id);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true) : null);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
