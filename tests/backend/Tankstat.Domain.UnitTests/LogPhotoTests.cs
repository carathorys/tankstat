using Tankstat.Domain.Images;
using Tankstat.Domain.Photos;

namespace Tankstat.Domain.UnitTests;

public class LogPhotoTests
{
    [Fact]
    public void Create_KeepsTheLogItBelongsTo()
    {
        var (owner, vehicle, log, image, by) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        var photo = LogPhoto.Create(owner, vehicle, LogType.Expense, log, image, by, now);

        Assert.NotEqual(Guid.Empty, photo.Id);
        Assert.Equal((owner, vehicle, LogType.Expense, log, image, by, now), (photo.OwnerId, photo.VehicleId, photo.LogType, photo.LogId, photo.ImageId, photo.CreatedById, photo.CreatedAt));
    }

    [Fact]
    public void Create_RejectsAnUnknownKindOfLog()
    {
        var error = Assert.Throws<DomainException>(() => LogPhoto.Create(Guid.NewGuid(), Guid.NewGuid(), (LogType)99, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal("photo.unknownLogType", error.Key);
    }

    [Fact]
    public void Folders_AreBuiltFromIdsOnly_AndNestUnderTheVehicle()
    {
        var (user, vehicle, log) = (Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"));

        Assert.Equal("users/11111111111111111111111111111111", ImageFolders.Avatar(user));
        Assert.Equal("vehicles/22222222222222222222222222222222", ImageFolders.Vehicle(vehicle));
        Assert.Equal("vehicles/22222222222222222222222222222222/picture", ImageFolders.VehiclePicture(vehicle));
        Assert.Equal("vehicles/22222222222222222222222222222222/expenses/33333333333333333333333333333333", ImageFolders.LogPhotos(vehicle, LogType.Expense, log));
        Assert.Equal("vehicles/22222222222222222222222222222222/refuelings/33333333333333333333333333333333", ImageFolders.LogPhotos(vehicle, LogType.Refueling, log));
        Assert.StartsWith(ImageFolders.Vehicle(vehicle) + "/", ImageFolders.LogPhotos(vehicle, LogType.Expense, log));
    }
}
