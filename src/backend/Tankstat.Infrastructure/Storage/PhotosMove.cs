using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;

namespace Tankstat.Infrastructure.Storage;

/// <summary>
/// Moves the photos of logs an older version kept with the pictures (<c>Storage:Path</c>) to their own root (<c>Storage:PhotosPath</c>), at
/// start, after the database migrations and before any request: for every vehicle, the files below its <c>drafts</c>, <c>refuelings</c> and
/// <c>expenses</c> folders, one by one (a folder cannot be moved from one disk to another), to the same place below the photos root. It
/// runs on every start and finds nothing once everything is moved. A file that cannot be moved stays where it was, where the store still
/// finds it, and is logged; nothing here ever stops the app.
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
                    logger.LogWarning(e, "A photo could not be moved to the photos folder; it stays with the pictures, where it is still found");
                }
            }
            RemoveEmpty(folder);
        }
        logger.LogInformation("Moved {Moved} photos of logs to their own folder; {Failed} could not be moved and stay with the pictures", moved, failed);
        return (moved, failed);
    }

    /// <summary>Removes the folders the move left empty, deepest first; one that is not empty stays.</summary>
    private static void RemoveEmpty(string folder)
    {
        foreach (var sub in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(folder))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
            }
            catch (IOException)
            {
                // something was put there meanwhile: it stays
            }
        }
    }
}
