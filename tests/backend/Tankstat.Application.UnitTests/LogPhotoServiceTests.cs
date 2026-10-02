using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Refuelings;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class LogPhotoServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    private static byte[] Jpeg(byte marker = 0) => [0xFF, 0xD8, 0xFF, 0xE0, marker, 1, 2, 3];

    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car, Expense Expense, Refueling Refueling);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, MeasurementUnits.Metric, default);
        var expense = await w.ExpenseService.AddAsync(car.Id, new ExpenseInput(Day, "Oil change", "Service", 100, "EUR", 1000, null), default);
        var refueling = await w.RefuelingService.LogAsync(car.Id, new RefuelingInput(Day.AddDays(1), 40, 60, "EUR", 1100, true, null), default);
        return new Scene(w, alice, bob, car, expense, refueling);
    }

    [Theory]
    [InlineData(LogType.Expense)]
    [InlineData(LogType.Refueling)]
    public async Task APhoto_IsStoredInTheFolderOfItsLog_ListedAndServed(LogType type)
    {
        var s = await Setup();
        var logId = type == LogType.Expense ? s.Expense.Id : s.Refueling.Id;

        var id = await s.W.Photos.AddAsync(type, logId, Jpeg(), default);

        var kind = type == LogType.Expense ? "expenses" : "refuelings";
        Assert.Equal($"vehicles/{s.Car.Id:N}/{kind}/{logId:N}", s.W.ImageStore.Folders[id]);
        var photo = Assert.Single(await s.W.Photos.ListAsync(type, logId, default));
        Assert.Equal((id, s.Alice.Id, s.Car.Id), (photo.ImageId, photo.CreatedById, photo.VehicleId));
        await using var served = await s.W.ImageService.OpenAsync(id, default);
        Assert.Equal("image/jpeg", served!.ContentType);
    }

    [Fact]
    public async Task PhotosOfOneLog_DoNotShowUpOnAnother_AndAreListedOldestFirst()
    {
        var s = await Setup();
        var first = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(1), default);
        s.W.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(2), default);

        Assert.Equal([first, second], (await s.W.Photos.ListAsync(LogType.Expense, s.Expense.Id, default)).Select(p => p.ImageId));
        Assert.Empty(await s.W.Photos.ListAsync(LogType.Refueling, s.Expense.Id, default)); // same id, other kind of log
        Assert.Empty(await s.W.Photos.ListAsync(LogType.Refueling, s.Refueling.Id, default));
    }

    [Fact]
    public async Task ALogHoldsAtMostTenPhotos()
    {
        var s = await Setup();
        for (var i = 0; i < LogPhoto.MaxPerLog; i++) await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg((byte)i), default);

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(99), default));

        Assert.Equal("photo.tooMany", error.Key);
        Assert.Equal(LogPhoto.MaxPerLog, s.W.ImageStore.Files.Count);
        await s.W.Photos.AddAsync(LogType.Refueling, s.Refueling.Id, Jpeg(), default); // the limit is per log
    }

    [Fact]
    public async Task OnlyPicturesAreAccepted_AndNothingIsLeftBehind()
    {
        var s = await Setup();

        await Assert.ThrowsAsync<DomainException>(() => s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, "<svg/>"u8.ToArray(), default));

        Assert.Empty(s.W.ImageStore.Files);
        Assert.Empty(s.W.LogPhotos.Items);
    }

    [Fact]
    public async Task WhenTheRowCannotBeSaved_TheFileIsRemovedAgain()
    {
        var s = await Setup();
        s.W.LogPhotos.FailAdds = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(), default));

        Assert.Empty(s.W.ImageStore.Files);
        Assert.Empty(s.W.Images.Items);
    }

    [Fact]
    public async Task Remove_DeletesTheRowAndTheFile_ButOnlyOfThatLog()
    {
        var s = await Setup();
        var keep = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(1), default);
        var drop = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(2), default);

        var wrongLog = await Assert.ThrowsAsync<NotFoundException>(() => s.W.Photos.RemoveAsync(LogType.Refueling, s.Refueling.Id, drop, default));
        await s.W.Photos.RemoveAsync(LogType.Expense, s.Expense.Id, drop, default);

        Assert.Equal("photo.notFound", wrongLog.Key);
        Assert.Equal([keep], (await s.W.Photos.ListAsync(LogType.Expense, s.Expense.Id, default)).Select(p => p.ImageId));
        Assert.Equal([keep], s.W.ImageStore.Files.Keys);
        Assert.Null(await s.W.ImageService.OpenAsync(drop, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Photos.RemoveAsync(LogType.Expense, s.Expense.Id, drop, default)); // already gone
    }

    [Fact]
    public async Task APhotoFollowsTheAccessRulesOfTheVehiclesLogs()
    {
        var s = await Setup();
        var id = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(), default);

        // a stranger sees nothing, and the log "does not exist" for them
        s.W.Current.SignInAs(s.Bob);
        Assert.Empty(await s.W.Photos.ListAsync(LogType.Expense, s.Expense.Id, default));
        Assert.Null(await s.W.ImageService.OpenAsync(id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(5), default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Photos.RemoveAsync(LogType.Expense, s.Expense.Id, id, default));

        // a viewer may look, not change
        s.W.Current.SignInAs(s.Alice);
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        s.W.Current.SignInAs(s.Bob);
        Assert.Single(await s.W.Photos.ListAsync(LogType.Expense, s.Expense.Id, default));
        Assert.NotNull(await s.W.ImageService.OpenAsync(id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(5), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.Photos.RemoveAsync(LogType.Expense, s.Expense.Id, id, default));

        // an editor may do both
        s.W.Grants.Items.Clear();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        var added = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(5), default);
        await s.W.Photos.RemoveAsync(LogType.Expense, s.Expense.Id, added, default);
    }

    [Fact]
    public async Task ALogGrantOnTheVehicleIsEnough()
    {
        var s = await Setup();
        var id = await s.W.Photos.AddAsync(LogType.Refueling, s.Refueling.Id, Jpeg(), default);
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        s.W.Current.SignInAs(s.Bob);

        Assert.NotNull(await s.W.ImageService.OpenAsync(id, default));
        await s.W.Photos.AddAsync(LogType.Refueling, s.Refueling.Id, Jpeg(7), default);
    }

    [Fact]
    public async Task PhotosOfATrashedLog_AreHidden_AndCannotBeChanged_UntilItIsRestored()
    {
        var s = await Setup();
        var id = await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(), default);
        await s.W.ExpenseService.DeleteAsync(s.Expense.Id, default);

        Assert.Null(await s.W.ImageService.OpenAsync(id, default));
        Assert.Empty(await s.W.Photos.ListAsync(LogType.Expense, s.Expense.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(2), default));

        await s.W.ExpenseService.RestoreAsync(s.Expense.Id, default);
        Assert.NotNull(await s.W.ImageService.OpenAsync(id, default));
    }

    [Theory]
    [InlineData(LogType.Expense)]
    [InlineData(LogType.Refueling)]
    public async Task EmptyingTheTrash_RemovesThePhotoFilesOfThePurgedLogsOnly(LogType type)
    {
        var s = await Setup();
        var (doomed, kept) = type == LogType.Expense
            ? (s.Expense.Id, (await s.W.ExpenseService.AddAsync(s.Car.Id, new ExpenseInput(Day.AddDays(2), "Wash", null, 10, "EUR", null, null), default)).Id)
            : (s.Refueling.Id, (await s.W.RefuelingService.LogAsync(s.Car.Id, new RefuelingInput(Day.AddDays(2), 30, 45, "EUR", 1200, true, null), default)).Id);
        await s.W.Photos.AddAsync(type, doomed, Jpeg(1), default);
        var keptPhoto = await s.W.Photos.AddAsync(type, kept, Jpeg(2), default);
        if (type == LogType.Expense) await s.W.ExpenseService.DeleteAsync(doomed, default);
        else await s.W.RefuelingService.DeleteAsync(doomed, default);

        var removed = type == LogType.Expense ? await s.W.ExpenseService.EmptyTrashAsync(default) : await s.W.RefuelingService.EmptyTrashAsync(default);

        Assert.Equal(1, removed);
        Assert.Equal([keptPhoto], s.W.ImageStore.Files.Keys);
        Assert.Equal([keptPhoto], s.W.Images.Items.Keys);
    }

    [Fact]
    public async Task PurgingAVehicle_RemovesItsWholeFolder_IncludingLogPhotos()
    {
        var s = await Setup();
        await s.W.Photos.AddAsync(LogType.Expense, s.Expense.Id, Jpeg(1), default);
        await s.W.Photos.AddAsync(LogType.Refueling, s.Refueling.Id, Jpeg(2), default);
        var picture = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(3), default);
        var elsewhere = await s.W.ImageService.SetAvatarAsync(Jpeg(4), default);
        await s.W.VehicleService.DeleteAsync(s.Car.Id, default);

        await s.W.VehicleService.EmptyTrashAsync(default);

        Assert.Equal([elsewhere], s.W.ImageStore.Files.Keys);
        Assert.Equal([elsewhere], s.W.Images.Items.Keys);
        Assert.False(s.W.ImageStore.Files.ContainsKey(picture));
    }
}
