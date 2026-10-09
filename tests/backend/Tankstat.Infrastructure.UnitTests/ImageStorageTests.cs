using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Images;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Images;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Storage;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public sealed class ImageStorageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-store-{Guid.NewGuid():N}");

    /// <summary>The photos of logs: a root of their own (unset, it would be the shared <c>photos</c> next to every test's folder).</summary>
    private readonly string _photos = Path.Combine(Path.GetTempPath(), $"tankstat-store-{Guid.NewGuid():N}-photos");

    public void Dispose()
    {
        foreach (var folder in new[] { _folder, _photos })
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private FileSystemImageStore Store(string? path = null, string? photos = null) =>
        new(Options.Create(new StorageOptions { Path = path ?? _folder, PhotosPath = photos ?? _photos }));

    private static string In(string root, string folder, StoredImage image) =>
        Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar), image.Id.ToString("N"));

    private static StoredImage Image(string? folder = null) =>
        StoredImage.Create(Guid.NewGuid(), "image/png", 1, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), folder);

    private static async Task<byte[]> ReadAll(Stream stream)
    {
        await using var s = stream;
        using var copy = new MemoryStream();
        await s.CopyToAsync(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task SavesReadsAndDeletes_CreatingTheFolderOnDemand()
    {
        var store = Store(Path.Combine(_folder, "nested", "deeper"));
        var image = Image();
        Assert.False(Directory.Exists(_folder));

        await store.SaveAsync(image, new byte[] { 1, 2, 3 }, default);

        Assert.Equal(new byte[] { 1, 2, 3 }, await ReadAll((await store.OpenReadAsync(image, default))!));
        await store.DeleteAsync(image, default);
        Assert.Null(await store.OpenReadAsync(image, default));
    }

    [Fact]
    public async Task ReplacingAFile_IsAtomic_AndLeavesNoTemporaryFiles()
    {
        var store = Store();
        var image = Image();
        await store.SaveAsync(image, new byte[] { 1 }, default);

        await store.SaveAsync(image, new byte[] { 2, 2 }, default);

        Assert.Equal(new byte[] { 2, 2 }, await ReadAll((await store.OpenReadAsync(image, default))!));
        Assert.Equal([image.Id.ToString("N")], Directory.GetFiles(_folder).Select(Path.GetFileName)); // named after the id, nothing else (no *.tmp)
    }

    [Fact]
    public async Task MissingFiles_AreNull_AndDeletingThemIsFine()
    {
        var store = Store();

        Assert.Null(await store.OpenReadAsync(Image(), default));
        await store.DeleteAsync(Image(), default);
        await store.DeleteFolderAsync("vehicles/" + Guid.NewGuid().ToString("N"), default);
    }

    [Fact]
    public async Task RelativePaths_ResolveAgainstTheWorkingDirectory_AndFilesStayInsideTheFolder()
    {
        var store = Store(_folder);
        var images = Enumerable.Range(0, 5).Select(_ => Image()).ToList();

        foreach (var image in images) await store.SaveAsync(image, new byte[] { 9 }, default);

        Assert.All(Directory.GetFiles(_folder), f => Assert.Equal(_folder, Path.GetDirectoryName(f)));
        Assert.Equal(5, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public async Task Files_LiveInTheirFolder_AndALegacyFileWithoutFolderStaysInTheDataFolder()
    {
        var store = Store();
        var vehicleId = Guid.NewGuid();
        var picture = Image(ImageFolders.VehiclePicture(vehicleId));
        var legacy = Image();

        await store.SaveAsync(picture, new byte[] { 1 }, default);
        await store.SaveAsync(legacy, new byte[] { 2 }, default);

        Assert.True(File.Exists(Path.Combine(_folder, "vehicles", vehicleId.ToString("N"), "picture", picture.Id.ToString("N"))));
        Assert.True(File.Exists(Path.Combine(_folder, legacy.Id.ToString("N"))));
        Assert.Equal(new byte[] { 1 }, await ReadAll((await store.OpenReadAsync(picture, default))!));
    }

    [Fact]
    public async Task DeletingAVehicleFolder_RemovesEverythingBelowIt_AndNothingElse()
    {
        var store = Store();
        var vehicleId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var picture = Image(ImageFolders.VehiclePicture(vehicleId));
        var photo = Image($"{ImageFolders.Vehicle(vehicleId)}/expenses/{Guid.NewGuid():N}");
        var othersPicture = Image(ImageFolders.VehiclePicture(other));
        foreach (var image in new[] { picture, photo, othersPicture }) await store.SaveAsync(image, new byte[] { 1 }, default);

        await store.DeleteFolderAsync(ImageFolders.Vehicle(vehicleId), default);

        Assert.Null(await store.OpenReadAsync(picture, default));
        Assert.Null(await store.OpenReadAsync(photo, default));
        Assert.NotNull(await store.OpenReadAsync(othersPicture, default));
        Assert.False(Directory.Exists(Path.Combine(_folder, "vehicles", vehicleId.ToString("N"))));
        Assert.False(Directory.Exists(Path.Combine(_photos, "vehicles", vehicleId.ToString("N")))); // its photos' tree too
        Assert.True(Directory.Exists(Path.Combine(_folder, "vehicles", other.ToString("N"))));
    }

    [Fact]
    public async Task MovingAFile_PutsItIntoTheOtherFolder_UnderTheSameName()
    {
        var store = Store();
        var vehicleId = Guid.NewGuid();
        var draft = Image(ImageFolders.PhotoDrafts(vehicleId));
        await store.SaveAsync(draft, new byte[] { 7, 7 }, default);
        var logFolder = $"{ImageFolders.Vehicle(vehicleId)}/refuelings/{Guid.NewGuid():N}";

        await store.MoveAsync(draft, logFolder, default);

        Assert.Null(await store.OpenReadAsync(draft, default)); // no longer in the drafts folder
        var moved = StoredImage.Create(draft.Id, draft.ContentType, draft.SizeBytes, draft.CreatedAt, logFolder);
        Assert.Equal(new byte[] { 7, 7 }, await ReadAll((await store.OpenReadAsync(moved, default))!));
        Assert.True(File.Exists(In(_photos, logFolder, draft)));
    }

    [Fact]
    public async Task PicturesAndThePhotosOfLogs_LiveUnderRootsOfTheirOwn_WithTheSameLayout()
    {
        var store = Store();
        var (vehicleId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        var picture = Image(ImageFolders.VehiclePicture(vehicleId));
        var avatar = Image(ImageFolders.Avatar(userId));
        var draft = Image(ImageFolders.PhotoDrafts(vehicleId));
        var photo = Image(ImageFolders.LogPhotos(vehicleId, LogType.Expense, Guid.NewGuid()));

        foreach (var image in new[] { picture, avatar, draft, photo }) await store.SaveAsync(image, new byte[] { 1 }, default);

        Assert.True(File.Exists(In(_folder, picture.Folder!, picture)));
        Assert.True(File.Exists(In(_folder, avatar.Folder!, avatar)));
        Assert.True(File.Exists(In(_photos, draft.Folder!, draft)));
        Assert.True(File.Exists(In(_photos, photo.Folder!, photo)));
        Assert.False(Directory.Exists(Path.Combine(_folder, "vehicles", vehicleId.ToString("N"), "drafts")));
    }

    [Fact]
    public async Task ADraftMadeTheVehiclesPicture_MovesToThePicturesRoot_AndBackToTheDrafts()
    {
        var store = Store();
        var vehicleId = Guid.NewGuid();
        var draft = Image(ImageFolders.PhotoDrafts(vehicleId));
        await store.SaveAsync(draft, new byte[] { 4, 2 }, default);
        var asPicture = StoredImage.Create(draft.Id, draft.ContentType, draft.SizeBytes, draft.CreatedAt, ImageFolders.VehiclePicture(vehicleId));

        await store.MoveAsync(draft, ImageFolders.VehiclePicture(vehicleId), default);

        Assert.True(File.Exists(In(_folder, ImageFolders.VehiclePicture(vehicleId), draft)));
        Assert.False(File.Exists(In(_photos, ImageFolders.PhotoDrafts(vehicleId), draft)));
        Assert.Equal(new byte[] { 4, 2 }, await ReadAll((await store.OpenReadAsync(asPicture, default))!));

        await store.MoveAsync(asPicture, ImageFolders.PhotoDrafts(vehicleId), default); // the vehicle could not be saved: back to the drafts

        Assert.True(File.Exists(In(_photos, ImageFolders.PhotoDrafts(vehicleId), draft)));
        Assert.Null(await store.OpenReadAsync(asPicture, default));
    }

    [Fact]
    public async Task APhotoAnOlderVersionLeftInThePicturesRoot_IsStillRead_Moved_AndDeleted()
    {
        var store = Store();
        var vehicleId = Guid.NewGuid();
        var draft = Image(ImageFolders.PhotoDrafts(vehicleId));
        var photo = Image(ImageFolders.LogPhotos(vehicleId, LogType.Refueling, Guid.NewGuid()));
        foreach (var (image, bytes) in new[] { (draft, new byte[] { 1 }), (photo, new byte[] { 2 }) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(In(_folder, image.Folder!, image))!);
            await File.WriteAllBytesAsync(In(_folder, image.Folder!, image), bytes); // where everything lived before the photos had a root
        }

        Assert.Equal(new byte[] { 2 }, await ReadAll((await store.OpenReadAsync(photo, default))!));

        var logFolder = ImageFolders.LogPhotos(vehicleId, LogType.Expense, Guid.NewGuid());
        await store.MoveAsync(draft, logFolder, default); // attached to a log: it lands where it belongs
        Assert.True(File.Exists(In(_photos, logFolder, draft)));
        Assert.False(File.Exists(In(_folder, draft.Folder!, draft)));

        await store.DeleteAsync(photo, default);
        Assert.False(File.Exists(In(_folder, photo.Folder!, photo)));
        Assert.Null(await store.OpenReadAsync(photo, default));
    }

    [Fact]
    public async Task WithOneFolderForBoth_EverythingStaysInIt()
    {
        var store = Store(photos: _folder);
        var vehicleId = Guid.NewGuid();
        var picture = Image(ImageFolders.VehiclePicture(vehicleId));
        var photo = Image(ImageFolders.LogPhotos(vehicleId, LogType.Refueling, Guid.NewGuid()));
        foreach (var image in new[] { picture, photo }) await store.SaveAsync(image, new byte[] { 1 }, default);

        Assert.True(File.Exists(In(_folder, photo.Folder!, photo)));
        await store.DeleteAsync(photo, default);
        Assert.Null(await store.OpenReadAsync(photo, default));
        await store.DeleteFolderAsync(ImageFolders.Vehicle(vehicleId), default);
        Assert.False(Directory.Exists(Path.Combine(_folder, "vehicles", vehicleId.ToString("N"))));
        Assert.False(Directory.Exists(_photos));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("vehicles/../../x")]
    [InlineData("/etc")]
    [InlineData("vehicles//x")]
    [InlineData("Vehicles")]
    [InlineData("vehicles/not-an-id.png")]
    [InlineData("vehicles")] // would be every vehicle's uploads
    [InlineData("users")]
    [InlineData("vehicles/00000000000000000000000000000000/other")]
    [InlineData("vehicles/00000000000000000000000000000000/drafts/x")] // drafts have no subfolders
    [InlineData("users/00000000000000000000000000000000\n")] // $ would let a trailing newline through
    public async Task Folders_ThatCouldEscapeTheDataFolder_AreRefused(string folder)
    {
        var store = Store();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Image(folder), new byte[] { 1 }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteFolderAsync(folder, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.MoveAsync(Image(), folder, default));
    }

    [Fact]
    public async Task ImageRows_RemoveByFolder_TakesTheFolderAndEverythingBelow_Only()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IImageRepository>();
        var vehicleId = Guid.NewGuid();
        var mine = new[] { Image(ImageFolders.VehiclePicture(vehicleId)), Image($"{ImageFolders.Vehicle(vehicleId)}/expenses/{Guid.NewGuid():N}") };
        var others = new[] { Image(ImageFolders.VehiclePicture(Guid.NewGuid())), Image() };
        foreach (var image in mine.Concat(others)) await repo.AddAsync(image, default);

        await repo.RemoveFolderAsync(ImageFolders.Vehicle(vehicleId), default);

        Assert.All(mine, i => Assert.Null(repo.FindAsync(i.Id, default).Result));
        Assert.All(others, i => Assert.NotNull(repo.FindAsync(i.Id, default).Result));
    }

    [Fact]
    public async Task ImageRows_RoundTripAndAreRemoved()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IImageRepository>();
        var image = StoredImage.Create(Guid.NewGuid(), "image/webp", 1234, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), "vehicles/00000000000000000000000000000001/picture");

        await repo.AddAsync(image, default);
        var loaded = (await repo.FindAsync(image.Id, default))!;
        await repo.RemoveAsync(image.Id, default);
        await repo.RemoveAsync(image.Id, default); // already gone: fine

        Assert.Equal(("image/webp", 1234L, image.CreatedAt, image.Folder), (loaded.ContentType, loaded.SizeBytes, loaded.CreatedAt, loaded.Folder));
        Assert.Null(await repo.FindAsync(image.Id, default));
    }

    [Fact]
    public async Task ImageRows_RememberANewFolder()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IImageRepository>();
        var vehicleId = Guid.NewGuid();
        var image = StoredImage.Create(Guid.NewGuid(), "image/webp", 10, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), ImageFolders.PhotoDrafts(vehicleId));
        await repo.AddAsync(image, default);
        var logFolder = $"{ImageFolders.Vehicle(vehicleId)}/expenses/{Guid.NewGuid():N}";

        image.MoveTo(logFolder);
        await repo.UpdateFolderAsync(image, default);

        Assert.Equal(logFolder, (await repo.FindAsync(image.Id, default))!.Folder);
    }

    [Fact]
    public async Task AvatarAndPictureLookups_FindTheOwnerOfAnImage()
    {
        await using var db = new TestDatabase();
        var users = db.Get<IUserRepository>();
        var vehicles = db.Get<IVehicleRepository>();
        var user = User.CreateLocal("a@x.co", null, false);
        await users.AddAsync(user, default);
        var car = TestData.Vehicle(user.Id);
        await vehicles.AddAsync(car, default);
        var avatar = Guid.NewGuid();
        var picture = Guid.NewGuid();

        var loadedUser = (await users.FindByIdAsync(user.Id, default))!;
        loadedUser.SetAvatar(avatar);
        await users.UpdateAsync(loadedUser, default);
        var loadedCar = (await vehicles.FindAsync(car.Id, default))!;
        loadedCar.SetPicture(picture);
        await vehicles.UpdateAsync(loadedCar, default);

        Assert.Equal(user.Id, (await users.FindByAvatarImageAsync(avatar, default))!.Id);
        Assert.Equal(car.Id, (await vehicles.FindByPictureImageAsync(picture, default))!.Id);
        Assert.Null(await users.FindByAvatarImageAsync(picture, default));
        Assert.Null(await vehicles.FindByPictureImageAsync(avatar, default));

        loadedCar.MarkDeleted(DateTimeOffset.UtcNow);
        await vehicles.UpdateAsync(loadedCar, default);
        Assert.Null(await vehicles.FindByPictureImageAsync(picture, default)); // a trashed vehicle's picture is not served
    }

    [Fact]
    public async Task PurgingVehicles_ReportsThePicturesToDelete()
    {
        await using var db = new TestDatabase();
        var vehicles = db.Get<IVehicleRepository>();
        var withPicture = TestData.Vehicle(Guid.NewGuid(), "A");
        var without = TestData.Vehicle(withPicture.OwnerId, "B");
        await vehicles.AddAsync(withPicture, default);
        await vehicles.AddAsync(without, default);
        var picture = Guid.NewGuid();
        foreach (var v in new[] { withPicture, without })
        {
            var loaded = (await vehicles.FindAsync(v.Id, default))!;
            if (v == withPicture) loaded.SetPicture(picture);
            loaded.MarkDeleted(DateTimeOffset.UtcNow);
            await vehicles.UpdateAsync(loaded, default);
        }

        var result = await vehicles.PurgeAsync(OwnerScope.All, default);

        Assert.Equal(2, result.Count);
        Assert.Equal([picture], result.ImageIds);
        Assert.Equivalent(new[] { withPicture.Id, without.Id }, result.VehicleIds);
    }
}
