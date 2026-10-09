using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;

namespace Tankstat.Infrastructure.Storage;

/// <summary>
/// Keeps picture bytes as plain files named after the image id (a GUID) inside the folder the image names (see <see cref="ImageFolders"/>),
/// below one of two roots: the pictures (<c>Storage:Path</c>, in Docker /data/uploads) or the photos of logs (<c>Storage:PhotosPath</c>,
/// opt-in; unset, the same folder), which <see cref="ImageFolders.RootsOf"/> tells apart. Folders are built from ids only, and are checked again here so
/// nothing user-controlled can ever reach a path. Files from before folders existed (no folder) sit directly in the pictures root. Photos
/// still in the pictures root (kept there before <c>Storage:PhotosPath</c> was set, not moved yet) are found there: read, deleted and moved.
/// </summary>
internal sealed partial class FileSystemImageStore(IOptions<StorageOptions> options) : IImageStore
{
    /// <summary>
    /// Exactly the folders <see cref="ImageFolders"/> builds (a user, a vehicle, below a vehicle its picture, its photo drafts or the photos of one log):
    /// no dots, no empty segments, nothing that can climb out, and never a bare "vehicles" or "users" that would take every upload.
    /// </summary>
    [GeneratedRegex(@"^(users/[0-9a-f]{32}|vehicles/[0-9a-f]{32}(/(picture|drafts|(expenses|refuelings)/[0-9a-f]{32}))?)\z")]
    private static partial Regex SafeFolder();

    private string RootOf(ImageRoots root) => root == ImageRoots.Photos ? options.Value.PhotosRoot : options.Value.PicturesRoot;

    /// <summary>Every root a folder may be found under, its own first: a photo folder may still be in the pictures root (not moved yet).</summary>
    private IEnumerable<string> RootsOf(string? folder)
    {
        var roots = ImageFolders.RootsOf(folder);
        if (roots.HasFlag(ImageRoots.Photos)) yield return RootOf(ImageRoots.Photos);
        if (!options.Value.OneRoot || !roots.HasFlag(ImageRoots.Photos)) yield return RootOf(ImageRoots.Pictures);
    }

    private static string FolderPath(string root, string? folder)
    {
        if (folder is null) return root;
        if (!SafeFolder().IsMatch(folder)) throw new ArgumentException($"Not a valid storage folder: '{folder}'.", nameof(folder));
        return Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FilePath(string root, StoredImage image) => Path.Combine(FolderPath(root, image.Folder), image.Id.ToString("N"));

    /// <summary>Where an image belongs (what is written goes there).</summary>
    private string PathFor(StoredImage image) => FilePath(RootsOf(image.Folder).First(), image);

    /// <summary>Where an image is: where it belongs, else in the pictures root (not moved yet); null when it is nowhere.</summary>
    private string? ExistingPathFor(StoredImage image) => RootsOf(image.Folder).Select(root => FilePath(root, image)).FirstOrDefault(File.Exists);

    /// <summary>
    /// Removes under every root the folder may be found under: one that fails (a locked file, a disk gone read-only) does not keep the
    /// other root from being cleaned, and its error is thrown once both were tried.
    /// </summary>
    private void RemoveUnderEveryRoot(string? folder, Action<string> remove)
    {
        Exception? failure = null;
        foreach (var root in RootsOf(folder))
        {
            try
            {
                remove(root);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failure ??= e;
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }

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
        var path = ExistingPathFor(image);
        return Task.FromResult<Stream?>(path is null ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));
    }

    public Task DeleteAsync(StoredImage image, CancellationToken ct)
    {
        RemoveUnderEveryRoot(image.Folder, root =>
        {
            var path = FilePath(root, image);
            if (File.Exists(path)) File.Delete(path);
        });
        return Task.CompletedTask;
    }

    /// <summary>Also from one root to the other (a draft made a vehicle's picture, and back): between two disks the file is copied, then deleted.</summary>
    public Task MoveAsync(StoredImage image, string folder, CancellationToken ct)
    {
        var source = ExistingPathFor(image) ?? PathFor(image); // nowhere: File.Move says so
        var targetFolder = FolderPath(RootsOf(folder).First(), folder);
        Directory.CreateDirectory(targetFolder);
        File.Move(source, Path.Combine(targetFolder, image.Id.ToString("N")), overwrite: true);
        return Task.CompletedTask;
    }

    /// <summary>Under every root the folder may be found under: a vehicle's folder goes from both.</summary>
    public Task DeleteFolderAsync(string folder, CancellationToken ct)
    {
        RemoveUnderEveryRoot(folder, root =>
        {
            var path = FolderPath(root, folder);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        });
        return Task.CompletedTask;
    }
}
