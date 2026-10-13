using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;
using Tankstat.Application.Images;

namespace Tankstat.Infrastructure.Storage;

/// <summary>Bytes of everything below the pictures root and, when the photos of logs have a root of their own, below that one.</summary>
internal sealed class StorageUsage(IOptions<StorageOptions> options) : IStorageUsage
{
    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };

    public Task<FileUsage> MeasureAsync(CancellationToken ct)
    {
        var storage = options.Value;
        return Task.Run(() => new FileUsage(Bytes(storage.PicturesRoot, ct), storage.OneRoot ? null : Bytes(storage.PhotosRoot, ct)), ct);
    }

    private static long Bytes(string root, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return 0;
        long total = 0;
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", Everything))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                total += file.Length;
            }
            catch (FileNotFoundException)
            {
                // gone meanwhile
            }
        }
        return total;
    }
}
