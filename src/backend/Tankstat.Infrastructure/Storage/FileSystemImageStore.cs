using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;

namespace Tankstat.Infrastructure.Storage;

/// <summary>
/// Keeps picture bytes as plain files named after the image id (a GUID) inside the folder the image names (see <see cref="ImageFolders"/>)
/// in the data folder (<c>Storage:Path</c>, in Docker the /data volume). Folders are built from ids only, and are checked again here
/// so nothing user-controlled can ever reach a path. Files from before folders existed (no folder) sit directly in the data folder.
/// </summary>
internal sealed partial class FileSystemImageStore(IOptions<StorageOptions> options) : IImageStore
{
    /// <summary>Lower-case words and 32-digit ids joined by "/": no dots, no empty segments, nothing that can climb out.</summary>
    [GeneratedRegex("^[a-z]+(/([a-z]+|[0-9a-f]{32}))*$")]
    private static partial Regex SafeFolder();

    private string Root => Path.GetFullPath(options.Value.Path);

    private string FolderPath(string? folder)
    {
        if (folder is null) return Root;
        if (!SafeFolder().IsMatch(folder)) throw new ArgumentException($"Not a valid storage folder: '{folder}'.", nameof(folder));
        return Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar));
    }

    private string PathFor(StoredImage image) => Path.Combine(FolderPath(image.Folder), image.Id.ToString("N"));

    public async Task SaveAsync(StoredImage image, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var target = PathFor(image);
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

    public Task<Stream?> OpenReadAsync(StoredImage image, CancellationToken ct)
    {
        var path = PathFor(image);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true) : null);
    }

    public Task DeleteAsync(StoredImage image, CancellationToken ct)
    {
        var path = PathFor(image);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string folder, CancellationToken ct)
    {
        var path = FolderPath(folder);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }
}
