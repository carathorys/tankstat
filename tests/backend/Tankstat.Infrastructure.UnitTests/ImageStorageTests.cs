using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Images;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Images;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Storage;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public sealed class ImageStorageTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-store-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private FileSystemImageStore Store(string? path = null) => new(Options.Create(new StorageOptions { Path = path ?? _folder }));

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
        var id = Guid.NewGuid();
        Assert.False(Directory.Exists(_folder));

        await store.SaveAsync(id, new byte[] { 1, 2, 3 }, default);

        Assert.Equal(new byte[] { 1, 2, 3 }, await ReadAll((await store.OpenReadAsync(id, default))!));
        await store.DeleteAsync(id, default);
        Assert.Null(await store.OpenReadAsync(id, default));
    }

    [Fact]
    public async Task ReplacingAFile_IsAtomic_AndLeavesNoTemporaryFiles()
    {
        var store = Store();
        var id = Guid.NewGuid();
        await store.SaveAsync(id, new byte[] { 1 }, default);

        await store.SaveAsync(id, new byte[] { 2, 2 }, default);

        Assert.Equal(new byte[] { 2, 2 }, await ReadAll((await store.OpenReadAsync(id, default))!));
        Assert.Equal([id.ToString("N")], Directory.GetFiles(_folder).Select(Path.GetFileName)); // named after the id, nothing else (no *.tmp)
    }

    [Fact]
    public async Task MissingFiles_AreNull_AndDeletingThemIsFine()
    {
        var store = Store();

        Assert.Null(await store.OpenReadAsync(Guid.NewGuid(), default));
        await store.DeleteAsync(Guid.NewGuid(), default);
    }

    [Fact]
    public async Task RelativePaths_ResolveAgainstTheWorkingDirectory_AndFilesStayInsideTheFolder()
    {
        var store = Store(_folder);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids) await store.SaveAsync(id, new byte[] { 9 }, default);

        Assert.All(Directory.GetFiles(_folder), f => Assert.Equal(_folder, Path.GetDirectoryName(f)));
        Assert.Equal(5, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public async Task ImageRows_RoundTripAndAreRemoved()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IImageRepository>();
        var image = StoredImage.Create(Guid.NewGuid(), "image/webp", 1234, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

        await repo.AddAsync(image, default);
        var loaded = (await repo.FindAsync(image.Id, default))!;
        await repo.RemoveAsync(image.Id, default);
        await repo.RemoveAsync(image.Id, default); // already gone: fine

        Assert.Equal(("image/webp", 1234L, image.CreatedAt), (loaded.ContentType, loaded.SizeBytes, loaded.CreatedAt));
        Assert.Null(await repo.FindAsync(image.Id, default));
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
    }
}
