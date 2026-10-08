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
    public async Task AVehicleAddTheServerRefuses_AndALogOfAVehicleThatIsGone_AreParked_AndTheRestOfTheBatchGoesOn()
    {
        var people = await _app.Users();
        var refused = Guid.NewGuid().ToString(); // an add the server refuses: that vehicle never exists
        var purged = Guid.NewGuid().ToString(); // as if purged meanwhile
        var car = Guid.NewGuid().ToString();

        var result = await Send(people.Alice,
            new { id = Guid.NewGuid(), addVehicle = new { id = refused, name = "", fuelType = "PETROL" } },
            new { id = Guid.NewGuid(), logRefueling = new { id = Guid.NewGuid(), vehicleId = purged, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } },
            new { id = Guid.NewGuid(), addVehicle = new { id = car, name = "Golf", fuelType = "PETROL" } });

        var statuses = result.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ToList();
        Assert.Equal(["PARKED", "PARKED", "APPLIED"], statuses); // filed with the sender: there is no vehicle row to file them under
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
}
