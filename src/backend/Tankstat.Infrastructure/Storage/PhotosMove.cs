using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;

namespace Tankstat.Infrastructure.Storage;

/// <summary>
/// Once <c>Storage:PhotosPath</c> is set (opt-in), moves the photos of logs kept with the pictures (<c>Storage:Path</c>) to their own root, at
/// start, after the database migrations and before any request: for every vehicle, the files below its <c>drafts</c>, <c>refuelings</c> and
/// <c>expenses</c> folders, one by one (a folder cannot be moved from one disk to another), to the same place below the photos root. It
/// runs on every start and finds nothing once everything is moved. A file that cannot be moved stays where it was, where the store still
/// finds it (a part of it a copy cut short left at the target goes), and is logged, as is a folder that cannot be read; the other files
/// move all the same, and nothing here ever stops the app.
/// </summary>
internal sealed class PhotosMove(IOptions<StorageOptions> options, ILogger<PhotosMove> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            Run();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The photos of logs could not be moved to their own folder; they are still served from the pictures folder");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Moves what is waiting; returns how many files were moved and how many could not be.</summary>
    internal (int Moved, int Failed) Run()
    {
        var storage = options.Value;
        if (storage.OneRoot) return (0, 0);
        var vehicles = Path.Combine(storage.PicturesRoot, "vehicles");
        if (!Directory.Exists(vehicles)) return (0, 0);

        var waiting = Directory.EnumerateDirectories(vehicles)
            .Where(vehicle => Guid.TryParseExact(Path.GetFileName(vehicle), "N", out _))
            .SelectMany(vehicle => ImageFolders.PhotoKinds.Select(kind => Path.Combine(vehicle, kind)))
            .Where(Directory.Exists)
            .ToList();
        if (waiting.Count == 0) return (0, 0);

        logger.LogInformation("Moving the photos of logs of {Vehicles} vehicles to their own folder", waiting.Select(Path.GetDirectoryName).Distinct().Count());
        var (moved, failed) = (0, 0);
        foreach (var folder in waiting)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList())
                {
                    var target = Path.Combine(storage.PhotosRoot, Path.GetRelativePath(storage.PicturesRoot, file));
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Move(file, target, overwrite: true); // between two disks: copied, then deleted
                        moved++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        RemovePartialCopy(file, target);
                        logger.LogWarning(e, "A photo could not be moved to the photos folder; it stays with the pictures, where it is still found");
                    }
                }
                RemoveEmpty(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read (left by another user, say): what is in it stays where the store finds it, the next ones move.
                failed++;
                logger.LogWarning(e, "A folder of photos could not be read; its photos stay with the pictures, where they are still found");
            }
        }
        logger.LogInformation("Moved {Moved} photos of logs to their own folder; {Failed} could not be moved and stay with the pictures", moved, failed);
        return (moved, failed);
    }

    /// <summary>
    /// Between two disks a move is a copy, and one cut short (a full disk) leaves part of the photo at the target, where the store looks
    /// first. The whole photo is still at the source, so that part goes. A target as long as the source stays: a whole copy (only the
    /// source could not be removed), or the very same file seen through another path, which must not be deleted.
    /// </summary>
    private static void RemovePartialCopy(string source, string target)
    {
        try
        {
            if (File.Exists(source) && File.Exists(target) && new FileInfo(target).Length != new FileInfo(source).Length) File.Delete(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // it stays; the warning that follows says the photo was not moved
        }
    }

    /// <summary>Removes the folders the move left empty, deepest first; one that is not empty, or may not be removed, stays.</summary>
    private static void RemoveEmpty(string folder)
    {
        foreach (var sub in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(folder))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // something was put there meanwhile, or it is not ours to remove: it stays
            }
        }
    }
}
