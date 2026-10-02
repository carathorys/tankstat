using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class PhotoDraftServiceTests
{
    private static byte[] Jpeg(byte marker = 0) => [0xFF, 0xD8, 0xFF, 0xE0, marker, 1, 2, 3];

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
}
