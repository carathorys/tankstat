using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Per-vehicle log access and the "only Delete may delete for good" rule, with real signed-in users.</summary>
[Collection(ApiCollection.Name)]
public class SharingAndDeleteLevelTests : IDisposable
{
    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private sealed record Users(HttpClient Admin, HttpClient Alice, HttpClient Bob, HttpClient Carol, string AliceId, string BobId, string CarolId);

    private async Task<Users> SignedInUsers()
    {
        var admin = _app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");

        async Task<(HttpClient Client, string Id)> Create(string name)
        {
            var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }", new { i = new { email = $"{name}@example.com", displayName = name, isAdmin = false } })).Data().GetProperty("createUser");
            await _app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }", new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = name + "-password-1" } });
            var client = _app.NewClient();
            await client.LoginAs($"{name}@example.com", name + "-password-1");
            return (client, created.GetProperty("user").GetProperty("id").GetString()!);
        }

        var alice = await Create("alice");
        var bob = await Create("bob");
        var carol = await Create("carol");
        return new Users(admin, alice.Client, bob.Client, carol.Client, alice.Id, bob.Id, carol.Id);
    }

    private static async Task<string> AddVehicle(HttpClient owner) =>
        (await owner.Gql("mutation { addVehicle(input: { name: \"Shared car\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;

    private static Task<JsonElement> Log(HttpClient c, string vehicleId, long odometer, string date = "2026-09-01") =>
        c.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id createdBy { displayName } } }",
            new { i = new { vehicleId, date, volume = 40, totalCost = 60, currency = "EUR", odometer, isFullTank = true } });

    private static Task<JsonElement> Share(HttpClient c, string vehicleId, string userId, string level) =>
        c.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId, userId, level } });

    private static string? Key(JsonElement body) =>
        body.TryGetProperty("errors", out var e) ? e[0].GetProperty("extensions").GetProperty("key").GetString() : null;

    [Fact]
    public async Task StrangersSeeNothing_AndCannotLog()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        await Log(u.Alice, car, 1000);

        Assert.Equal(0, (await u.Bob.Gql("{ vehicleCount }")).Data().GetProperty("vehicleCount").GetInt32());
        Assert.Equal(0, (await u.Bob.Gql("query($id: UUID!) { refuelings(vehicleId: $id) { id } }", new { id = car })).Data().GetProperty("refuelings").GetArrayLength());
        Assert.Equal("vehicle.notFound", Key(await Log(u.Bob, car, 2000)));
        Assert.Equal("vehicle.notFound", Key(await Share(u.Bob, car, u.CarolId, "EDIT")));
    }

    [Fact]
    public async Task ALogGrant_LetsABobWorkWithTheLogs_ButNotChangeTheVehicle()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        Assert.True((await Share(u.Alice, car, u.BobId, "EDIT")).Data().GetProperty("setVehicleLogAccess").GetBoolean());

        // Bob now sees the vehicle, as a log editor who is not allowed to edit the vehicle
        var seen = (await u.Bob.Gql("{ vehicles { id name canEdit logAccess owner { displayName } } vehicleCount }")).Data();
        var row = Assert.Single(seen.GetProperty("vehicles").EnumerateArray());
        Assert.Equal((false, "EDIT", "alice"), (row.GetProperty("canEdit").GetBoolean(), row.GetProperty("logAccess").GetString(), row.GetProperty("owner").GetProperty("displayName").GetString()));
        Assert.Equal(1, seen.GetProperty("vehicleCount").GetInt32());

        // he can add, change and trash logs; the log records who made it
        var made = (await Log(u.Bob, car, 1000)).Data().GetProperty("logRefueling");
        Assert.Equal("bob", made.GetProperty("createdBy").GetProperty("displayName").GetString());
        var logId = made.GetProperty("id").GetString();
        Assert.Null(Key(await u.Bob.Gql("mutation($i: UpdateRefuelingInput!) { updateRefueling(input: $i) { id } }", new { i = new { id = logId, date = "2026-09-01", volume = 41, totalCost = 61, odometer = 1010, isFullTank = true } })));
        Assert.Null(Key(await u.Bob.Gql("mutation($id: UUID!) { deleteRefueling(id: $id) { id } }", new { id = logId })));
        Assert.Equal(1, (await u.Alice.Gql("{ refuelingTrashCount }")).Data().GetProperty("refuelingTrashCount").GetInt32()); // Alice sees Bob's trashed log too

        // but not the vehicle itself, nor sharing, nor permanent deletion
        Assert.Equal("vehicle.viewOnly", Key(await u.Bob.Gql("mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { id } }", new { i = new { id = car, name = "Hacked", fuelType = "LPG" } })));
        Assert.Equal("vehicle.viewOnly", Key(await u.Bob.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = car })));
        Assert.Equal("share.editRequired", Key(await Share(u.Bob, car, u.CarolId, "EDIT")));
        Assert.Equal(0, (await u.Bob.Gql("mutation { emptyRefuelingTrash }")).Data().GetProperty("emptyRefuelingTrash").GetInt32());
        Assert.Equal(0, (await u.Bob.Gql("{ refuelingTrashDeletableCount }")).Data().GetProperty("refuelingTrashDeletableCount").GetInt32());
        Assert.Equal(1, (await u.Bob.Gql("{ refuelingTrashCount }")).Data().GetProperty("refuelingTrashCount").GetInt32()); // may restore
    }

    [Fact]
    public async Task OnlyDeleteLevel_MayEmptyTheTrashOfLogs()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        await Share(u.Alice, car, u.BobId, "EDIT");
        await Share(u.Alice, car, u.CarolId, "DELETE");
        var logId = (await Log(u.Alice, car, 1000)).Data().GetProperty("logRefueling").GetProperty("id").GetString();
        await u.Alice.Gql("mutation($id: UUID!) { deleteRefueling(id: $id) { id } }", new { id = logId });

        Assert.Equal(0, (await u.Bob.Gql("mutation { emptyRefuelingTrash }")).Data().GetProperty("emptyRefuelingTrash").GetInt32());
        Assert.Equal(1, (await u.Carol.Gql("{ refuelingTrashDeletableCount }")).Data().GetProperty("refuelingTrashDeletableCount").GetInt32());
        Assert.Equal(1, (await u.Carol.Gql("mutation { emptyRefuelingTrash }")).Data().GetProperty("emptyRefuelingTrash").GetInt32());
        Assert.Equal(0, (await u.Alice.Gql("{ refuelingTrashCount }")).Data().GetProperty("refuelingTrashCount").GetInt32());
    }

    [Fact]
    public async Task OwnersAndAdministrators_MayDeleteForGood_EditorsMayNot_ForVehiclesToo()
    {
        var u = await SignedInUsers();
        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.BobId, level = "EDIT" } });
        var car = await AddVehicle(u.Alice);
        await u.Bob.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = car }); // an owner-wide editor may trash it

        Assert.Equal(1, (await u.Bob.Gql("{ trashCount trashDeletableCount }")).Data().GetProperty("trashCount").GetInt32());
        Assert.Equal(0, (await u.Bob.Gql("{ trashDeletableCount }")).Data().GetProperty("trashDeletableCount").GetInt32());
        Assert.Equal(0, (await u.Bob.Gql("mutation { emptyTrash }")).Data().GetProperty("emptyTrash").GetInt32());
        Assert.Equal(1, (await u.Admin.Gql("{ trashDeletableCount }")).Data().GetProperty("trashDeletableCount").GetInt32());

        // grant Delete on the owner's data: now Bob may
        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.BobId, level = "DELETE" } });
        Assert.Equal(1, (await u.Bob.Gql("mutation { emptyTrash }")).Data().GetProperty("emptyTrash").GetInt32());
    }

    [Fact]
    public async Task ADeleteGrantOnTheOwnersData_AlsoMeansDeleteOnTheLogs()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        var logId = (await Log(u.Alice, car, 1000)).Data().GetProperty("logRefueling").GetProperty("id").GetString();
        await u.Alice.Gql("mutation($id: UUID!) { deleteRefueling(id: $id) { id } }", new { id = logId });
        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.CarolId, level = "DELETE" } });

        Assert.Equal(1, (await u.Carol.Gql("mutation { emptyRefuelingTrash }")).Data().GetProperty("emptyRefuelingTrash").GetInt32());
    }

    [Fact]
    public async Task Sharing_ListsCandidates_AndGrants_AndRevokes()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        await Share(u.Alice, car, u.BobId, "EDIT");

        var candidates = (await u.Alice.Gql("query($id: UUID!) { shareCandidates(vehicleId: $id) { id displayName } }", new { id = car })).Data().GetProperty("shareCandidates");
        Assert.Contains(candidates.EnumerateArray(), c => c.GetProperty("displayName").GetString() == "carol");
        Assert.DoesNotContain(candidates.EnumerateArray(), c => c.GetProperty("displayName").GetString() is "alice" or "bob"); // not the owner, nobody who has access already

        await Share(u.Alice, car, u.BobId, "DELETE"); // upgrade
        var access = (await u.Alice.Gql("query($id: UUID!) { vehicleLogAccess(vehicleId: $id) { level user { displayName } } }", new { id = car })).Data().GetProperty("vehicleLogAccess");
        var entry = Assert.Single(access.EnumerateArray());
        Assert.Equal(("DELETE", "bob"), (entry.GetProperty("level").GetString(), entry.GetProperty("user").GetProperty("displayName").GetString()));

        await Share(u.Alice, car, u.BobId, "NONE");
        Assert.Equal(0, (await u.Alice.Gql("query($id: UUID!) { vehicleLogAccess(vehicleId: $id) { level } }", new { id = car })).Data().GetProperty("vehicleLogAccess").GetArrayLength());
        Assert.Equal(0, (await u.Bob.Gql("{ vehicleCount }")).Data().GetProperty("vehicleCount").GetInt32()); // access is gone at once
    }

    [Fact]
    public async Task AnEditor_CannotGrantMoreThanTheyHold_AndInvalidLevelsAreRejected()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.BobId, level = "EDIT" } });

        Assert.Null(Key(await Share(u.Bob, car, u.CarolId, "EDIT")));
        Assert.Equal("share.cannotGrantMore", Key(await Share(u.Bob, car, u.CarolId, "DELETE")));
        Assert.Equal("share.levelRequired", Key(await Share(u.Alice, car, u.CarolId, "VIEW")));
        Assert.Equal("share.ownerHasAccess", Key(await Share(u.Admin, car, u.AliceId, "EDIT")));
    }

    [Fact]
    public async Task ALogGrant_AppliesToThatVehicleOnly()
    {
        var u = await SignedInUsers();
        var shared = await AddVehicle(u.Alice);
        var other = await AddVehicle(u.Alice);
        await Share(u.Alice, shared, u.BobId, "EDIT");

        Assert.Null(Key(await Log(u.Bob, shared, 100)));
        Assert.Equal("vehicle.notFound", Key(await Log(u.Bob, other, 100)));
        Assert.Equal(1, (await u.Bob.Gql("{ vehicleCount }")).Data().GetProperty("vehicleCount").GetInt32());
    }

    [Fact]
    public async Task ViewOnlyUsers_ReadLogs_ButCannotChangeThem()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        var logId = (await Log(u.Alice, car, 1000)).Data().GetProperty("logRefueling").GetProperty("id").GetString();
        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.BobId, level = "VIEW" } });

        Assert.Equal(1, (await u.Bob.Gql("query($id: UUID!) { refuelingCount(vehicleId: $id) }", new { id = car })).Data().GetProperty("refuelingCount").GetInt32());
        var row = (await u.Bob.Gql("query($id: UUID!) { refuelings(vehicleId: $id) { canEdit canDelete } }", new { id = car })).Data().GetProperty("refuelings")[0];
        Assert.False(row.GetProperty("canEdit").GetBoolean() || row.GetProperty("canDelete").GetBoolean());
        Assert.Equal("vehicle.viewOnly", Key(await Log(u.Bob, car, 2000)));
        Assert.Equal("vehicle.viewOnly", Key(await u.Bob.Gql("mutation($id: UUID!) { deleteRefueling(id: $id) { id } }", new { id = logId })));
    }

    [Fact]
    public async Task EverythingNeedsASignIn()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        var anonymous = _app.NewClient();

        Assert.Equal("auth.unauthenticated", Key(await anonymous.Gql("query($id: UUID!) { refuelings(vehicleId: $id) { id } }", new { id = car })));
        Assert.Equal("auth.unauthenticated", Key(await anonymous.Gql("{ refuelingTrashCount }")));
        Assert.Equal("auth.unauthenticated", Key(await Share(anonymous, car, u.BobId, "EDIT")));
    }
}
