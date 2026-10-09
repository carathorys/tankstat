using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Images;
using Tankstat.Domain.Images;
using Tankstat.Domain.Photos;
using Tankstat.Infrastructure.Storage;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public sealed class PhotosMoveTests : IDisposable
{
    private readonly string _pictures = Path.Combine(Path.GetTempPath(), $"tankstat-move-{Guid.NewGuid():N}-uploads");
    private readonly string _photos = Path.Combine(Path.GetTempPath(), $"tankstat-move-{Guid.NewGuid():N}-photos");
    private readonly CapturedLog _log = new();

    public void Dispose()
    {
        foreach (var folder in new[] { _pictures, _photos })
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private PhotosMove Move(string? photos = null) =>
        new(Options.Create(new StorageOptions { Path = _pictures, PhotosPath = photos ?? _photos }), _log.For<PhotosMove>());

    /// <summary>A file where an older version kept it: below the pictures root.</summary>
    private string Old(string folder, Guid? id = null)
    {
        var path = Path.Combine(_pictures, folder.Replace('/', Path.DirectorySeparatorChar), (id ?? Guid.NewGuid()).ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private string Moved(string oldPath) => Path.Combine(_photos, Path.GetRelativePath(_pictures, oldPath));

    [Fact]
    public void ThePhotosOfLogsAndTheDrafts_MoveToThePhotosRoot_ThePicturesStay_AndASecondRunFindsNothing()
    {
        var (car, van) = (Guid.NewGuid(), Guid.NewGuid());
        var photos = new[]
        {
            Old(ImageFolders.PhotoDrafts(car)),
            Old(ImageFolders.LogPhotos(car, LogType.Refueling, Guid.NewGuid())),
            Old(ImageFolders.LogPhotos(car, LogType.Refueling, Guid.NewGuid())),
            Old(ImageFolders.LogPhotos(van, LogType.Expense, Guid.NewGuid())),
        };
        var pictures = new[] { Old(ImageFolders.VehiclePicture(car)), Old(ImageFolders.Avatar(Guid.NewGuid())), Old("vehicles/" + van.ToString("N") + "/picture") };

        Assert.Equal((4, 0), Move().Run());

        Assert.All(photos, p => Assert.True(File.Exists(Moved(p)) && !File.Exists(p)));
        Assert.All(pictures, p => Assert.True(File.Exists(p)));
        Assert.False(Directory.Exists(Path.Combine(_pictures, "vehicles", car.ToString("N"), "refuelings"))); // emptied folders go too
        Assert.False(Directory.Exists(Path.Combine(_pictures, "vehicles", car.ToString("N"), "drafts")));
        Assert.Equal((0, 0), Move().Run());
        Assert.Equal(2, _log.From<PhotosMove>().Count(e => e.Level == LogLevel.Information)); // the first run only: before and after
        Assert.Equal(4, _log.From<PhotosMove>().Last().Values["Moved"]);
    }

    [Fact]
    public void AFileThatCannotBeMoved_StaysWithThePictures_IsLogged_AndTheRestMoves()
    {
        var car = Guid.NewGuid();
        var stuck = Old(ImageFolders.PhotoDrafts(car));
        var fine = Old(ImageFolders.LogPhotos(car, LogType.Expense, Guid.NewGuid()));
        Directory.CreateDirectory(Moved(stuck)); // a folder where the file should go: it cannot be moved there

        Assert.Equal((1, 1), Move().Run());

        Assert.True(File.Exists(stuck));
        Assert.True(File.Exists(Moved(fine)));
        var warning = Assert.Single(_log.From<PhotosMove>(), e => e.Level == LogLevel.Warning);
        Assert.NotNull(warning.Exception);
    }

    [Fact]
    public void WithOneFolderForBoth_OrNothingToMove_NothingHappens()
    {
        var photo = Old(ImageFolders.PhotoDrafts(Guid.NewGuid()));

        Assert.Equal((0, 0), Move(photos: _pictures).Run());
        Assert.True(File.Exists(photo));

        Directory.Delete(_pictures, recursive: true);
        Assert.Equal((0, 0), Move().Run()); // no pictures folder at all (a new installation)
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public void AFolderThatCannotBeRead_IsLogged_AndTheOtherVehiclesStillMove()
    {
        if (OperatingSystem.IsWindows()) return; // the folder below is locked the Unix way
        var cars = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var drafts = cars.Select(car => Old(ImageFolders.PhotoDrafts(car))).ToList();
        var locked = Path.GetDirectoryName(Old(ImageFolders.LogPhotos(cars[1], LogType.Expense, Guid.NewGuid())))!;
        File.SetUnixFileMode(locked, UnixFileMode.None); // left by another user, say: the app cannot look inside
        try
        {
            if (CanList(locked)) return; // root reads everything: nothing to show here

            Assert.Equal((3, 1), Move().Run());

            Assert.All(drafts, d => Assert.True(File.Exists(Moved(d))));
            Assert.Single(_log.From<PhotosMove>(), e => e.Level == LogLevel.Warning);
            Assert.Equal(3, _log.From<PhotosMove>().Last().Values["Moved"]); // the counts are still told
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void ACopyCutShort_LeavesNoPartOfThePhotoWhereTheStoreLooksFirst()
    {
        if (OperatingSystem.IsWindows()) return; // the folder below is made read-only the Unix way
        var photo = Old(ImageFolders.LogPhotos(Guid.NewGuid(), LogType.Refueling, Guid.NewGuid()));
        Directory.CreateDirectory(Path.GetDirectoryName(Moved(photo))!);
        File.WriteAllBytes(Moved(photo), [1]); // what a copy between two disks leaves when the disk fills up
        var folder = Path.GetDirectoryName(photo)!;
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute); // and this time the move fails
        try
        {
            if (CanWriteIn(folder)) return; // root may write anywhere: the move would just work

            Assert.Equal((0, 1), Move().Run());

            Assert.False(File.Exists(Moved(photo))); // so the store finds the whole photo where it was
            Assert.Equal([1, 2, 3], File.ReadAllBytes(photo));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool CanList(string folder)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(folder).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanWriteIn(string folder)
    {
        var probe = Path.Combine(folder, "probe");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Starting_NeverFails_EvenWhenThePhotosRootCannotBeCreated()
    {
        Old(ImageFolders.PhotoDrafts(Guid.NewGuid()));
        var blocked = Path.Combine(_photos, "file");
        Directory.CreateDirectory(_photos);
        await File.WriteAllTextAsync(blocked, "a file, not a folder");

        await Move(photos: blocked).StartAsync(default); // every move fails: the folder cannot be made below a file

        Assert.Contains(_log.From<PhotosMove>(), e => e.Level == LogLevel.Warning);
    }
}
