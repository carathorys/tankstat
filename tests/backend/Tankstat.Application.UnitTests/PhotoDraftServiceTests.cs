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

public class PhotoDraftServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    private static byte[] Jpeg(byte marker = 0) => [0xFF, 0xD8, 0xFF, 0xE0, marker, 1, 2, 3];

    private static RefuelingInput Fill(long odometer = 1000) => new(Day, 40, 60, "EUR", odometer, true, null);

    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car, Vehicle Van);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, MeasurementUnits.Metric, default);
        var van = await w.VehicleService.AddAsync("Van", null, FuelType.Diesel, MeasurementUnits.Metric, default);
        return new Scene(w, alice, bob, car, van);
    }

    // ---- a draft made the vehicle's picture (a picture chosen offline) ----------------------------------------

    [Fact]
    public async Task ADraft_BecomesTheVehiclesPicture_ReplacingTheOldOne_AndTheSameDraftAgainChangesNothing()
    {
        var s = await Setup();
        var old = await s.W.ImageService.SetVehiclePictureAsync(s.Car.Id, Jpeg(1), default);
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(2), default);

        var set = await s.W.ImageService.SetVehiclePictureFromDraftAsync(s.Car.Id, draft, default);
        var again = await s.W.ImageService.SetVehiclePictureFromDraftAsync(s.Car.Id, draft, default); // a change sent again

        Assert.Equal((draft, draft), (set, again));
        Assert.Equal(draft, (await s.W.Vehicles.FindAsync(s.Car.Id, default))!.PictureImageId);
        Assert.Equal($"vehicles/{s.Car.Id:N}/picture", s.W.ImageStore.Folders[draft]);
        Assert.Empty(s.W.PhotoDrafts.Items); // no longer a draft: its file is the picture now
        Assert.False(s.W.ImageStore.Folders.ContainsKey(old)); // the old picture is gone, as with an upload
    }

    [Fact]
    public async Task ADraftThatIsNotTheUsers_OfAnotherVehicle_OrExpired_IsRefused_AndChangesNothing()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        var vans = await s.W.Drafts.UploadAsync(s.Van.Id, Jpeg(), default);
        s.W.Current.SignInAs(s.Bob);
        var bobs = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);
        s.W.Current.SignInAs(s.Alice);
        var expired = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);
        s.W.Clock.Advance(PhotoDraft.Lifetime);

        foreach (var draft in new[] { vans, bobs, expired, Guid.NewGuid() })
            Assert.Equal("photo.draftExpired", (await Assert.ThrowsAsync<DomainException>(() => s.W.ImageService.SetVehiclePictureFromDraftAsync(s.Car.Id, draft, default))).Key);
        Assert.Null((await s.W.Vehicles.FindAsync(s.Car.Id, default))!.PictureImageId);
    }

    [Fact]
    public async Task MakingADraftThePicture_NeedsEditOnTheVehicleItself()
    {
        var s = await Setup();
        s.W.ResourceGrants.Items.Add(ResourceGrant.Create(ResourceType.Vehicle, s.Car.Id, s.Bob.Id, GrantedFeature.Logs, AccessLevel.Edit));
        s.W.Current.SignInAs(s.Bob);
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default); // a log editor may upload drafts ...

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ImageService.SetVehiclePictureFromDraftAsync(s.Car.Id, draft, default)); // ... not change the picture

        Assert.Equal("vehicle.viewOnly", refused.Key);
        Assert.Single(s.W.PhotoDrafts.Items);
    }

    // ---- uploading -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ADraft_IsStoredInTheVehiclesDraftFolder_AndOnlyItsUploaderSeesIt()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));

        var id = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);

        Assert.Equal($"vehicles/{s.Car.Id:N}/drafts", s.W.ImageStore.Folders[id]);
        var draft = Assert.Single(s.W.PhotoDrafts.Items);
        Assert.Equal((id, s.Alice.Id, s.Car.Id, s.Alice.Id), (draft.Id, draft.OwnerId, draft.VehicleId, draft.CreatedById));
        await using (var mine = await s.W.ImageService.OpenAsync(id, default)) Assert.NotNull(mine);
        s.W.Current.SignInAs(s.Bob); // may edit the vehicle's logs, but the draft is not part of any log yet
        Assert.Null(await s.W.ImageService.OpenAsync(id, default));
    }

    [Fact]
    public async Task Uploading_NeedsEditAccessToTheVehiclesLogs()
    {
        var s = await Setup();
        s.W.Current.SignInAs(s.Bob);

        var stranger = await Assert.ThrowsAsync<NotFoundException>(() => s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default));
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        var viewer = await Assert.ThrowsAsync<ForbiddenException>(() => s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default));
        s.W.ResourceGrants.Items.Add(ResourceGrant.Create(ResourceType.Vehicle, s.Car.Id, s.Bob.Id, GrantedFeature.Logs, AccessLevel.Edit));
        await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default); // a grant on the vehicle's logs is enough

        Assert.Equal(("vehicle.notFound", "vehicle.viewOnly"), (stranger.Key, viewer.Key));
        Assert.Single(s.W.PhotoDrafts.Items);
    }

    [Fact]
    public async Task Uploading_RefusesWhatIsNotAPicture()
    {
        var s = await Setup();

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.Drafts.UploadAsync(s.Car.Id, "not an image"u8.ToArray(), default));

        Assert.Equal("image.unsupportedType", error.Key);
        Assert.Empty(s.W.PhotoDrafts.Items);
        Assert.Empty(s.W.ImageStore.Files);
    }

    [Fact]
    public async Task EachUser_MayKeepALimitedNumberOfDraftsPerVehicle()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        for (var i = 0; i < PhotoDraft.MaxPerUserAndVehicle; i++) await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg((byte)i), default);

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(99), default));
        await s.W.Drafts.UploadAsync(s.Van.Id, Jpeg(), default); // another vehicle has its own room
        s.W.Current.SignInAs(s.Bob);
        await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default); // and so does another user

        Assert.Equal(("photo.tooManyDrafts", (object?)PhotoDraft.MaxPerUserAndVehicle), (error.Key, error.Args["max"]));
        Assert.Equal(PhotoDraft.MaxPerUserAndVehicle + 2, s.W.PhotoDrafts.Items.Count);
    }

    [Fact]
    public async Task Uploading_ClearsAwayExpiredDrafts_OfEveryVehicleAndUser()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        var alicesOld = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(1), default);
        s.W.Current.SignInAs(s.Bob);
        var bobsOld = await s.W.Drafts.UploadAsync(s.Van.Id, Jpeg(2), default);
        s.W.Clock.Advance(PhotoDraft.Lifetime - TimeSpan.FromMinutes(1));
        var recent = await s.W.Drafts.UploadAsync(s.Van.Id, Jpeg(3), default);
        s.W.Clock.Advance(TimeSpan.FromMinutes(2)); // the first two are now older than a day, the third is not

        s.W.Current.SignInAs(s.Alice);
        var fresh = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(4), default);

        Assert.Equal(new[] { recent, fresh }.Order(), s.W.PhotoDrafts.Items.Select(d => d.Id).Order());
        Assert.False(s.W.ImageStore.Files.ContainsKey(alicesOld));
        Assert.False(s.W.ImageStore.Files.ContainsKey(bobsOld));
        Assert.False(s.W.Images.Items.ContainsKey(bobsOld));
    }

    // ---- removing ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Removing_TakesTheDraftAndItsFile_ButOnlyTheUploadersOwn()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        var kept = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(1), default);
        var removed = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(2), default);

        await s.W.Drafts.RemoveAsync(removed, default);
        s.W.Current.SignInAs(s.Bob);
        var notHis = await Assert.ThrowsAsync<NotFoundException>(() => s.W.Drafts.RemoveAsync(kept, default));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => s.W.Drafts.RemoveAsync(Guid.NewGuid(), default));

        Assert.Equal(("photo.notFound", "photo.notFound"), (notHis.Key, unknown.Key));
        Assert.Equal([kept], s.W.PhotoDrafts.Items.Select(d => d.Id));
        Assert.Equal([kept], s.W.ImageStore.Files.Keys);
    }

    // ---- attaching on save ---------------------------------------------------------------------------------

    [Fact]
    public async Task SavingARefueling_MakesItsDraftsItsPhotos()
    {
        var s = await Setup();
        var first = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(1), default);
        var second = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(2), default);

        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, photoDraftIds: [first, second]);

        var photos = await s.W.Photos.ListAsync(LogType.Refueling, log.Id, default);
        Assert.Equal(new[] { first, second }.Order(), photos.Select(p => p.ImageId).Order());
        Assert.All(photos, p => Assert.Equal((s.Alice.Id, s.Car.Id, s.Alice.Id), (p.OwnerId, p.VehicleId, p.CreatedById)));
        var folder = $"vehicles/{s.Car.Id:N}/refuelings/{log.Id:N}";
        Assert.All(new[] { first, second }, id => Assert.Equal(folder, s.W.ImageStore.Folders[id]));
        Assert.Equal(folder, s.W.Images.Items[first].Folder);
        Assert.Empty(s.W.PhotoDrafts.Items);
    }

    [Fact]
    public async Task SavingAnExpense_MakesItsDraftsItsPhotos_AndTheyAreSeenLikeTheLog()
    {
        var s = await Setup();
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);

        var expense = await s.W.ExpenseService.AddAsync(s.Car.Id, new ExpenseInput(Day, "Tyres", null, 300, "EUR", null, null), default, [draft]);

        Assert.Equal([draft], (await s.W.Photos.ListAsync(LogType.Expense, expense.Id, default)).Select(p => p.ImageId));
        Assert.Equal($"vehicles/{s.Car.Id:N}/expenses/{expense.Id:N}", s.W.ImageStore.Folders[draft]);
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        s.W.Current.SignInAs(s.Bob); // a photo of the expense now: whoever may see the expense sees it
        await using var seen = await s.W.ImageService.OpenAsync(draft, default);
        Assert.NotNull(seen);
    }

    [Fact]
    public async Task Saving_WithADraftItMayNotTake_SavesNothing()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        var onTheVan = await s.W.Drafts.UploadAsync(s.Van.Id, Jpeg(1), default);
        var old = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(2), default);
        s.W.Current.SignInAs(s.Bob);
        var bobs = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(3), default);
        s.W.Current.SignInAs(s.Alice);
        s.W.Clock.Advance(PhotoDraft.Lifetime);

        Guid[][] attempts = [[onTheVan], [bobs], [old], [Guid.NewGuid()]];
        foreach (var ids in attempts)
        {
            var error = await Assert.ThrowsAsync<DomainException>(() => s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, photoDraftIds: ids));
            Assert.Equal("photo.draftExpired", error.Key);
        }

        Assert.Empty(s.W.Refuelings.Items);
        Assert.Empty(s.W.LogPhotos.Items);
        Assert.Equal(3, s.W.PhotoDrafts.Items.Count); // nothing was taken or thrown away
    }

    [Fact]
    public async Task Saving_TakesAtMostTenPhotos_AndTheSameDraftOnlyOnce()
    {
        var s = await Setup();
        var ids = new List<Guid>();
        for (var i = 0; i < LogPhoto.MaxPerLog + 1; i++) ids.Add(await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg((byte)i), default));

        var tooMany = await Assert.ThrowsAsync<DomainException>(() => s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, photoDraftIds: ids));
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, photoDraftIds: [ids[0], ids[0], ids[1]]);

        Assert.Equal("photo.tooMany", tooMany.Key);
        Assert.Equal(2, (await s.W.Photos.ListAsync(LogType.Refueling, log.Id, default)).Count);
    }

    [Fact]
    public async Task ARuleBrokenByTheLog_LeavesItsDraftsAsTheyWere()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(5000), default);
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.RefuelingService.LogAsync(s.Car.Id, Fill(4000) with { Date = Day.AddDays(1) }, default, photoDraftIds: [draft]));

        Assert.Equal("odometer.belowPrevious", error.Key);
        Assert.Equal([draft], s.W.PhotoDrafts.Items.Select(d => d.Id)); // still there for the corrected save
        Assert.Equal($"vehicles/{s.Car.Id:N}/drafts", s.W.ImageStore.Folders[draft]);
    }

    [Fact]
    public async Task APhotoThatCannotBeMoved_DoesNotFailTheSave_AndStaysADraft()
    {
        var s = await Setup();
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);
        s.W.ImageStore.FailMoves = true;

        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, photoDraftIds: [draft]);

        Assert.Single(s.W.Refuelings.Items);
        Assert.Empty(await s.W.Photos.ListAsync(LogType.Refueling, log.Id, default)); // the save went through; the photo did not make it
        Assert.Equal([draft], s.W.PhotoDrafts.Items.Select(d => d.Id));
        Assert.Equal($"vehicles/{s.Car.Id:N}/drafts", s.W.ImageStore.Folders[draft]);
    }

    [Fact]
    public async Task APhotoWhoseRowCannotBeSaved_GoesBackToTheDrafts()
    {
        var s = await Setup();
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);
        s.W.LogPhotos.FailAdds = true;

        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default, photoDraftIds: [draft]);

        Assert.Empty(await s.W.Photos.ListAsync(LogType.Refueling, log.Id, default));
        Assert.Equal($"vehicles/{s.Car.Id:N}/drafts", s.W.ImageStore.Folders[draft]); // moved back
        Assert.Equal($"vehicles/{s.Car.Id:N}/drafts", s.W.Images.Items[draft].Folder);
        Assert.Equal([draft], s.W.PhotoDrafts.Items.Select(d => d.Id));
    }

    // ---- a draft as a photo of a saved log (a photo added offline) ---------------------------------------------

    [Fact]
    public async Task ADraft_BecomesAPhotoOfASavedLog_OnceOnly()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default);
        var draft = await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default);

        var first = await s.W.Photos.AttachDraftAsync(LogType.Refueling, log.Id, draft, default);
        var again = await s.W.Photos.AttachDraftAsync(LogType.Refueling, log.Id, draft, default);

        Assert.Equal((draft, draft), (first, again));
        Assert.Equal([draft], (await s.W.Photos.ListAsync(LogType.Refueling, log.Id, default)).Select(p => p.ImageId));
        Assert.Empty(s.W.PhotoDrafts.Items);
        Assert.Equal($"vehicles/{s.Car.Id:N}/refuelings/{log.Id:N}", s.W.ImageStore.Folders[draft]);
        Assert.Equal(1, (await s.W.RefuelingService.FindAsync(log.Id, default))!.Version); // photos are not part of the log's version
    }

    [Fact]
    public async Task ADraft_OfAnotherVehicleOrUser_OrALogWithoutRoom_IsNotAttached()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(), default);
        var vans = await s.W.Drafts.UploadAsync(s.Van.Id, Jpeg(), default);

        var other = await Assert.ThrowsAsync<DomainException>(() => s.W.Photos.AttachDraftAsync(LogType.Refueling, log.Id, vans, default));
        for (byte i = 0; i < LogPhoto.MaxPerLog; i++) await s.W.Photos.AddAsync(LogType.Refueling, log.Id, Jpeg(i), default);
        var full = await Assert.ThrowsAsync<DomainException>(async () =>
            await s.W.Photos.AttachDraftAsync(LogType.Refueling, log.Id, await s.W.Drafts.UploadAsync(s.Car.Id, Jpeg(), default), default));
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.Edit));
        var bobs = await Assert.ThrowsAsync<DomainException>(async () =>
        {
            var logTwo = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(2000), default);
            s.W.Current.SignInAs(s.Bob);
            await s.W.Photos.AttachDraftAsync(LogType.Refueling, logTwo.Id, vans, default); // Alice's draft
        });

        Assert.Equal(("photo.draftExpired", "photo.tooMany", "photo.draftExpired"), (other.Key, full.Key, bobs.Key));
    }
}
