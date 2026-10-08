using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Changes a device kept offline, sent with <c>syncChanges</c>, over real GraphQL and a real database.</summary>
[Collection(ApiCollection.Name)]
public class SyncGraphQLTests : IDisposable
{
    private const string Sync = """
        mutation($i: SyncChangesInput!) { syncChanges(input: $i) { applied parked results { id status entityId version reason { key args { name value } } } } }
        """;

    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private static async Task<JsonElement> Send(HttpClient c, params object[] changes) =>
        (await c.Gql(Sync, new { i = new { changes } })).Data().GetProperty("syncChanges");

    private static async Task<int> Count(HttpClient c, string vehicleId) =>
        (await c.Gql("query($v: UUID!) { refuelingCount(vehicleId: $v) }", new { v = vehicleId })).Data().GetProperty("refuelingCount").GetInt32();

    [Fact]
    public async Task AVehicleAddedOffline_AndALogOfIt_AreApplied_AndTheSameBatchAgainAppliesNothing()
    {
        var people = await _app.Users();
        var car = Guid.NewGuid().ToString();
        var log = Guid.NewGuid().ToString();
        object[] batch =
        [
            new { id = Guid.NewGuid(), addVehicle = new { id = car, name = "Golf", fuelType = "PETROL" } },
            new { id = Guid.NewGuid(), logRefueling = new { id = log, vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } },
        ];

        var first = await Send(people.Alice, batch);
        var again = await Send(people.Alice, batch);

        Assert.Equal(2, first.GetProperty("applied").GetInt32());
        Assert.Equal(car, first.GetProperty("results")[0].GetProperty("entityId").GetString());
        Assert.Equal(log, first.GetProperty("results")[1].GetProperty("entityId").GetString());
        Assert.Equal(first.GetProperty("results").ToString(), again.GetProperty("results").ToString());
        Assert.Equal(1, await Count(people.Alice, car));
    }

    [Fact]
    public async Task AChangeMadeFromAnOldVersion_IsParked_AndChangesNothing()
    {
        var people = await _app.Users();
        var car = (await people.Alice.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        await people.Alice.Gql("mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { id } }", new { i = new { id = car, name = "Golf GTI", fuelType = "PETROL" } }); // version 2 now

        var result = await Send(people.Alice, new { id = Guid.NewGuid(), expectedVersion = 1, updateVehicle = new { id = car, name = "Polo", fuelType = "PETROL" } });

        var parked = result.GetProperty("results")[0];
        Assert.Equal("PARKED", parked.GetProperty("status").GetString());
        Assert.Equal("sync.versionMismatch", parked.GetProperty("reason").GetProperty("key").GetString());
        Assert.Equal("Golf GTI", (await people.Alice.Gql($"{{ vehicle(id: \"{car}\") {{ name }} }}")).Data().GetProperty("vehicle").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ARefusalOfTheServer_IsParkedWithItsKey_AndAccessStillApplies()
    {
        var people = await _app.Users();
        var car = (await people.Alice.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;

        var bobs = await Send(people.Bob, new { id = Guid.NewGuid(), logRefueling = new { id = Guid.NewGuid(), vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } });

        Assert.Equal("PARKED", bobs.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.Equal("vehicle.notFound", bobs.GetProperty("results")[0].GetProperty("reason").GetProperty("key").GetString());
        Assert.Equal(0, await Count(people.Alice, car));
    }

    [Fact]
    public async Task AChangeWithoutExactlyOneOperation_OrAnAddWithoutItsId_RefusesTheBatch()
    {
        var people = await _app.Users();
        var none = await people.Alice.Gql(Sync, new { i = new { changes = new object[] { new { id = Guid.NewGuid() } } } });
        var noId = await people.Alice.Gql(Sync, new { i = new { changes = new object[] { new { id = Guid.NewGuid(), addVehicle = new { name = "Golf", fuelType = "PETROL" } } } } });
        var signedOut = await _app.NewClient().Gql(Sync, new { i = new { changes = new object[] { new { id = Guid.NewGuid(), deleteVehicle = Guid.NewGuid() } } } });

        Assert.Equal("sync.oneOperationRequired", none.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Equal("sync.entityIdRequired", noId.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Equal("UNAUTHENTICATED", signedOut.ErrorCode());
    }

    private const string Parked = "{ parkedChanges { id kind status change reason { key } submittedBy { displayName } canResolve vehicleId } }";
    private const string Resolve = "mutation($i: ResolveSyncChangeInput!) { resolveSyncChange(input: $i) { id status entityId version } }";

    /// <summary>Bob (an editor of Alice's car) renames it from an old version on a device: parked, filed with the car.</summary>
    private static async Task<(TwoUsers People, string Car, string ChangeId)> ParkedByBob(TestApp app)
    {
        var people = await app.Users();
        var car = (await people.Alice.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var log = (await people.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data().GetProperty("logRefueling").GetProperty("id").GetString()!;
        await people.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = car, userId = people.BobId, level = "EDIT" } });
        await people.Alice.Gql("mutation($i: UpdateRefuelingInput!) { updateRefueling(input: $i) { id } }",
            new { i = new { id = log, date = "2026-09-01", volume = 41, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } }); // version 2
        var changeId = Guid.NewGuid().ToString();
        await Send(people.Bob, new { id = changeId, expectedVersion = 1, updateRefueling = new { id = log, date = "2026-09-01", volume = 45, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } });
        return (people, car, changeId);
    }

    [Fact]
    public async Task AParkedChange_IsListedForTheOwnerAndTheSender_CountedOnTheVehicle_AndNotified()
    {
        var (people, car, changeId) = await ParkedByBob(_app);

        var alices = (await people.Alice.Gql(Parked)).Data().GetProperty("parkedChanges");
        var only = Assert.Single(alices.EnumerateArray());
        Assert.Equal((changeId, "UPDATE_REFUELING", "sync.versionMismatch", "bob", true, car),
            (only.GetProperty("id").GetString(), only.GetProperty("kind").GetString(), only.GetProperty("reason").GetProperty("key").GetString(),
             only.GetProperty("submittedBy").GetProperty("displayName").GetString(), only.GetProperty("canResolve").GetBoolean(), only.GetProperty("vehicleId").GetString()));
        Assert.Contains("\"volume\":45", only.GetProperty("change").GetString());
        Assert.Single((await people.Bob.Gql(Parked)).Data().GetProperty("parkedChanges").EnumerateArray());
        Assert.Equal(1, (await people.Alice.Gql($"{{ vehicle(id: \"{car}\") {{ parkedChangeCount }} }}")).Data().GetProperty("vehicle").GetProperty("parkedChangeCount").GetInt32());

        var inbox = (await people.Alice.Gql("{ notifications(unreadOnly: true) { kind subject { type id } args { name value } } }")).Data().GetProperty("notifications");
        var note = inbox.EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "SYNC_CHANGE_PARKED");
        Assert.Equal(("SYNC_CHANGE", changeId), (note.GetProperty("subject").GetProperty("type").GetString(), note.GetProperty("subject").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AParkedChange_IsAppliedAnywayAsEdited_OrDiscarded_OnceOnly()
    {
        var (people, car, changeId) = await ParkedByBob(_app);
        var log = (await people.Alice.Gql($"{{ refuelings(vehicleId: \"{car}\", orderBy: DATE, direction: DESC, skip: 0, take: 5) {{ id }} }}"))
            .Data().GetProperty("refuelings")[0].GetProperty("id").GetString()!;

        var applied = (await people.Alice.Gql(Resolve, new { i = new { id = changeId, action = "APPLY", change = new { id = changeId,
            updateRefueling = new { id = log, date = "2026-09-01", volume = 44, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } } } })).Data().GetProperty("resolveSyncChange");
        var twice = await people.Alice.Gql(Resolve, new { i = new { id = changeId, action = "DISCARD" } });

        Assert.Equal(("APPLIED", 3), (applied.GetProperty("status").GetString(), applied.GetProperty("version").GetInt32()));
        Assert.Equal("sync.notParked", twice.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Empty((await people.Alice.Gql(Parked)).Data().GetProperty("parkedChanges").EnumerateArray());
        Assert.Equal(44, (await people.Alice.Gql($"{{ refueling(id: \"{log}\") {{ volume }} }}")).Data().GetProperty("refueling").GetProperty("volume").GetDecimal());
    }

    [Fact]
    public async Task OnlyThoseWhoMaySeeIt_FindAParkedChange_AndAnEditMustDoTheSame()
    {
        var (people, _, changeId) = await ParkedByBob(_app);
        var stranger = _app.NewClient();
        await people.Admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } } }", new { i = new { email = "eve@example.com", displayName = "eve", isAdmin = false } });

        var otherKind = await people.Alice.Gql(Resolve, new { i = new { id = changeId, action = "APPLY", change = new { id = changeId, deleteRefueling = Guid.NewGuid() } } });
        var discarded = (await people.Bob.Gql(Resolve, new { i = new { id = changeId, action = "DISCARD" } })).Data().GetProperty("resolveSyncChange");

        Assert.Equal("sync.kindMismatch", otherKind.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Equal("DISCARDED", discarded.GetProperty("status").GetString());
        Assert.Equal("UNAUTHENTICATED", (await stranger.Gql(Parked)).ErrorCode());
    }
}
