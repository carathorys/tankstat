namespace Tankstat.Api.IntegrationTests;

/// <summary>A user's own order of their vehicles on the home page, over real GraphQL and a real database.</summary>
[Collection(ApiCollection.Name)]
public class VehicleOrderApiTests : IDisposable
{
    private const string SetOrder = "mutation($ids: [UUID!]!) { setVehicleOrder(vehicleIds: $ids) }";

    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private static async Task<string> Car(HttpClient c, string name) =>
        (await c.Gql("mutation($n: String!) { addVehicle(input: { name: $n, fuelType: PETROL }) { id } }", new { n = name })).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;

    private static async Task<string[]> Names(HttpClient c, string? search = null) =>
        (await c.Gql("query($s: String) { myVehicles(search: $s) { name } }", new { s = search })).Data().GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task TheArrangement_OrdersTheHomeList_ForItsUserOnly()
    {
        var people = await _app.Users();
        var golf = await Car(people.Alice, "Golf");
        await Car(people.Alice, "Octavia");
        var passat = await Car(people.Alice, "Passat");
        await Car(people.Bob, "Bobs");
        Assert.Equal(["Golf", "Octavia", "Passat"], await Names(people.Alice));

        Assert.True((await people.Alice.Gql(SetOrder, new { ids = new[] { passat, golf } })).Data().GetProperty("setVehicleOrder").GetBoolean());

        Assert.Equal(["Passat", "Golf", "Octavia"], await Names(people.Alice)); // the arranged ones first, the rest by name
        Assert.Equal(3, (await people.Alice.Gql("{ myVehicleCount }")).Data().GetProperty("myVehicleCount").GetInt32());
        Assert.Equal(["Passat", "Octavia"], await Names(people.Alice, "a")); // the search still filters, the order stays
        Assert.Equal(["Bobs"], await Names(people.Bob));
    }

    [Fact]
    public async Task AVehicleTheUserMayNotSee_LooksNonExistent_AndNothingChanges()
    {
        var people = await _app.Users();
        var golf = await Car(people.Alice, "Golf");
        var octavia = await Car(people.Alice, "Octavia");
        var bobs = await Car(people.Bob, "Bobs");
        await people.Alice.Gql(SetOrder, new { ids = new[] { octavia, golf } });

        var refused = await people.Alice.Gql(SetOrder, new { ids = new[] { golf, bobs } });
        var tooMany = await people.Alice.Gql(SetOrder, new { ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid().ToString()).ToArray() });

        Assert.Equal("NOT_FOUND", refused.ErrorCode());
        Assert.Equal("vehicle.notFound", refused.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Equal("VALIDATION_FAILED", tooMany.ErrorCode());
        Assert.Equal(["Octavia", "Golf"], await Names(people.Alice)); // unchanged
    }

    [Fact]
    public async Task ASharedVehicleCanBePlaced_AndATrashedOneLeavesAndReturnsInPlace()
    {
        var people = await _app.Users();
        await Car(people.Alice, "Golf");
        var octavia = await Car(people.Alice, "Octavia");
        var bobs = await Car(people.Bob, "Bobs");
        await people.Bob.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = bobs, userId = people.AliceId, level = "EDIT" } });

        await people.Alice.Gql(SetOrder, new { ids = new[] { bobs, octavia } });
        Assert.Equal(["Bobs", "Octavia", "Golf"], await Names(people.Alice));

        await people.Alice.Gql($"mutation {{ deleteVehicle(id: \"{octavia}\") {{ id }} }}");
        Assert.Equal(["Bobs", "Golf"], await Names(people.Alice));

        await people.Alice.Gql($"mutation {{ restoreVehicle(id: \"{octavia}\") {{ id }} }}");
        Assert.Equal(["Bobs", "Octavia", "Golf"], await Names(people.Alice)); // its place was kept
    }
}
