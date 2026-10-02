using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Images;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class ImageFormatTests
{
    private static byte[] Jpeg(int size = 100) => [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[size]];
    private static byte[] Png(int size = 100) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[size]];
    private static byte[] WebP(int size = 100) => [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8, .. new byte[size]];

    [Fact]
    public void DetectsJpegPngAndWebP_FromTheBytesThemselves()
    {
        Assert.Equal("image/jpeg", ImageFormat.Detect(Jpeg()));
        Assert.Equal("image/png", ImageFormat.Detect(Png()));
        Assert.Equal("image/webp", ImageFormat.Detect(WebP()));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("GIF89a....")]
    [InlineData("not an image at all")]
    public void RejectsEverythingElse_EvenWithAFriendlyName(string content) =>
        Assert.Equal("image.unsupportedType", Assert.Throws<DomainException>(() => ImageFormat.Detect(System.Text.Encoding.UTF8.GetBytes(content))).Key);

    [Fact]
    public void RejectsRiffFilesThatAreNotWebP() =>
        Assert.Equal("image.unsupportedType", Assert.Throws<DomainException>(() => ImageFormat.Detect([.. "RIFF"u8, 1, 2, 3, 4, .. "WAVE"u8, 0, 0])).Key);

    [Fact]
    public void RejectsEmptyAndOversizedFiles()
    {
        Assert.Equal("image.empty", Assert.Throws<DomainException>(() => ImageFormat.Detect([])).Key);
        var tooLarge = Assert.Throws<DomainException>(() => ImageFormat.Detect(Jpeg(ImageFormat.MaxBytes)));
        Assert.Equal("image.tooLarge", tooLarge.Key);
        Assert.Equal(ImageFormat.MaxBytes / 1024, tooLarge.Args["maxKb"]);
    }

    [Fact]
    public void AcceptsAFileExactlyAtTheLimit() => Assert.Equal("image/png", ImageFormat.Detect(Png(ImageFormat.MaxBytes - 8)));
}

public class ImageServiceTests
{
    private static byte[] Jpeg(byte marker = 0) => [0xFF, 0xD8, 0xFF, 0xE0, marker, 1, 2, 3];

    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        return new Scene(w, alice, bob, await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, default));
    }

    // ---- avatars ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AUser_SetsAndReplacesTheirAvatar_TheOldFileIsRemoved()
    {
        var s = await Setup();

        var first = await s.W.ImageService.SetAvatarAsync(Jpeg(1), default);
        var second = await s.W.ImageService.SetAvatarAsync(Jpeg(2), default);

        Assert.Equal(second, s.Alice.AvatarImageId);
        Assert.NotEqual(first, second); // a new id, so caches never show the old picture
        Assert.False(s.W.ImageStore.Files.ContainsKey(first));
        Assert.False(s.W.Images.Items.ContainsKey(first));
        Assert.Equal("image/jpeg", s.W.Images.Items[second].ContentType);
        Assert.Equal(Jpeg(2).Length, s.W.Images.Items[second].SizeBytes);
    }

    [Fact]
    public async Task RemovingTheAvatar_DeletesTheFile()
    {
        var s = await Setup();
        var id = await s.W.ImageService.SetAvatarAsync(Jpeg(), default);

        await s.W.ImageService.RemoveAvatarAsync(default);

        Assert.Null(s.Alice.AvatarImageId);
        Assert.Empty(s.W.ImageStore.Files);
        Assert.Empty(s.W.Images.Items);
        Assert.False(s.W.ImageStore.Files.ContainsKey(id));
    }

    [Fact]
    public async Task InvalidFiles_ChangeNothing()
    {
        var s = await Setup();
        await s.W.ImageService.SetAvatarAsync(Jpeg(1), default);

        await Assert.ThrowsAsync<DomainException>(() => s.W.ImageService.SetAvatarAsync("<svg/>"u8.ToArray(), default));

        Assert.Single(s.W.ImageStore.Files);
        Assert.Single(s.W.Images.Items);
    }

    [Fact]
    public async Task ADatabaseFailure_DoesNotLeaveAnOrphanFile()
    {
        var s = await Setup();
        s.W.Images.FailAdds = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.W.ImageService.SetAvatarAsync(Jpeg(), default));

        Assert.Empty(s.W.ImageStore.Files);
        Assert.Null(s.Alice.AvatarImageId);
    }

    [Fact]
    public async Task WithoutAuthentication_ThereAreNoProfilePictures()
    {
        var w = new World(AuthMode.None);

        Assert.Equal("image.noAccount", (await Assert.ThrowsAsync<DomainException>(() => w.ImageService.SetAvatarAsync(Jpeg(), default))).Key);
    }

    [Fact]
    public async Task SignedOut_CannotUpload()
    {
        var s = await Setup();
        s.W.Current.Principal = null;

        await Assert.ThrowsAsync<UnauthenticatedException>(() => s.W.ImageService.SetAvatarAsync(Jpeg(), default));
    }

    [Fact]
    public async Task Avatars_AreVisibleToAnySignedInUser_ButNotToAnonymousVisitors()
    {
        var s = await Setup();
        var id = await s.W.ImageService.SetAvatarAsync(Jpeg(7), default);

        s.W.Current.SignInAs(s.Bob);
        await using (var image = await s.W.ImageService.OpenAsync(id, default))
        {
            Assert.NotNull(image);
            Assert.Equal("image/jpeg", image!.ContentType);
            using var copy = new MemoryStream();
            await image.Content.CopyToAsync(copy);
            Assert.Equal(Jpeg(7), copy.ToArray());
        }

        s.W.Current.Principal = null;
        await Assert.ThrowsAsync<UnauthenticatedException>(() => s.W.ImageService.OpenAsync(id, default));
    }

    [Fact]
    public async Task UnknownImages_AreNull() => Assert.Null(await (await Setup()).W.ImageService.OpenAsync(Guid.NewGuid(), default));

    // ---- vehicle pictures ------------------------------------------------------------------------------------

    [Fact]
    public async Task AnOwner_SetsAndRemovesTheVehiclePicture()
    {
        var s = await Setup();

        var id = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);
        Assert.Equal(id, s.Car.PictureImageId);
        await s.W.ImageService.RemoveVehiclePictureAsync(s.Car.Id, default);

        Assert.Null(s.Car.PictureImageId);
        Assert.Empty(s.W.ImageStore.Files);
    }

    [Fact]
    public async Task VehiclePictures_FollowTheVehiclesAccess()
    {
        var s = await Setup();
        var id = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);

        s.W.Current.SignInAs(s.Bob);
        Assert.Null(await s.W.ImageService.OpenAsync(id, default)); // cannot see the vehicle
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default));

        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        Assert.NotNull(await s.W.ImageService.OpenAsync(id, default)); // may see it ...
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default)); // ... not change it
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ImageService.RemoveVehiclePictureAsync(s.Car.Id, default));
    }

    [Fact]
    public async Task ALogGrantee_SeesThePicture_ButCannotChangeIt()
    {
        var s = await Setup();
        var id = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        s.W.Current.SignInAs(s.Bob);

        Assert.NotNull(await s.W.ImageService.OpenAsync(id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default));
    }

    [Fact]
    public async Task AnEditor_MayChangeThePicture()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        s.W.Current.SignInAs(s.Bob);

        await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);

        Assert.NotNull(s.Car.PictureImageId);
    }

    [Fact]
    public async Task APicturesOfATrashedVehicle_AreHidden_AndDeletedForGoodWithTheVehicle()
    {
        var s = await Setup();
        var id = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);
        await s.W.VehicleService.DeleteAsync(s.Car.Id, default);

        Assert.Null(await s.W.ImageService.OpenAsync(id, default)); // trashed: invisible
        Assert.Equal(1, await s.W.VehicleService.EmptyTrashAsync(default));

        Assert.Empty(s.W.ImageStore.Files); // the picture was removed with the vehicle
        Assert.Empty(s.W.Images.Items);
    }

    [Fact]
    public async Task Pictures_OfVehiclesThatStayInTheTrash_AreKeptForARestore()
    {
        var s = await Setup();
        var id = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);
        await s.W.VehicleService.DeleteAsync(s.Car.Id, default);
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        s.W.Current.SignInAs(s.Bob);

        Assert.Equal(0, await s.W.VehicleService.EmptyTrashAsync(default)); // an editor may not delete for good

        Assert.True(s.W.ImageStore.Files.ContainsKey(id));
    }

    [Fact]
    public async Task NewPictures_GoIntoTheFolderOfTheirVehicleOrUser()
    {
        var s = await Setup();

        var picture = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);
        var avatar = await s.W.ImageService.SetAvatarAsync(Jpeg(1), default);

        Assert.Equal($"vehicles/{s.Car.Id:N}/picture", s.W.ImageStore.Folders[picture]);
        Assert.Equal($"vehicles/{s.Car.Id:N}/picture", s.W.Images.Items[picture].Folder);
        Assert.Equal($"users/{s.Alice.Id:N}", s.W.ImageStore.Folders[avatar]);
    }

    [Fact]
    public async Task ReplacingAPicture_RemovesTheOldFileFromTheSameFolder()
    {
        var s = await Setup();
        var first = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(), default);

        var second = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(1), default);

        Assert.False(s.W.ImageStore.Files.ContainsKey(first));
        Assert.Equal($"vehicles/{s.Car.Id:N}/picture", s.W.ImageStore.Folders[second]);
    }

    [Fact]
    public async Task APictureFromBeforeFolders_StillWorks_AndIsDeletedWithItsVehicle()
    {
        var s = await Setup();
        var legacy = Guid.NewGuid();
        s.W.Images.Items[legacy] = Tankstat.Domain.Images.StoredImage.Create(legacy, "image/jpeg", 8, s.W.Clock.GetUtcNow()); // no folder
        s.W.ImageStore.Files[legacy] = Jpeg();
        s.W.ImageStore.Folders[legacy] = null;
        s.Car.SetPicture(legacy);

        Assert.NotNull(await s.W.ImageService.OpenAsync(legacy, default));
        await s.W.VehicleService.DeleteAsync(s.Car.Id, default);
        await s.W.VehicleService.EmptyTrashAsync(default);

        Assert.Empty(s.W.ImageStore.Files);
        Assert.Empty(s.W.Images.Items);
    }
}
