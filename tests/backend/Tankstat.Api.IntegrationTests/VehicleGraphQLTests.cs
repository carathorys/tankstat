using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class VehicleGraphQLTests(ApiFixture api)
{
    private async Task<JsonElement> Send(string query, object? variables = null)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task AddVehicle_LogRefueling_AndReadBack()
    {
        var added = await Send(
            "mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id name licensePlate fuelType } }",
            new { i = new { name = "Octavia", licensePlate = "abc-123", fuelType = "DIESEL" } });
        Assert.False(added.TryGetProperty("errors", out _), added.ToString());
        var vehicle = added.GetProperty("data").GetProperty("addVehicle");
        Assert.Equal("ABC-123", vehicle.GetProperty("licensePlate").GetString());
        var id = vehicle.GetProperty("id").GetString();

        var logged = await Send(
            "mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = id, date = "2026-10-01", volume = 41.5, totalCost = 79.9, odometer = 12000, isFullTank = true } });
        Assert.False(logged.TryGetProperty("errors", out _), logged.ToString());

        var read = await Send(
            "query($id: UUID!) { vehicle(id: $id) { name } refuelings(vehicleId: $id) { date volume totalCost currency odometer isFullTank pricePerUnit } }",
            new { id });
        Assert.False(read.TryGetProperty("errors", out _), read.ToString());
        var refueling = read.GetProperty("data").GetProperty("refuelings")[0];
        Assert.Equal("2026-10-01", refueling.GetProperty("date").GetString());
        Assert.Equal(41.5m, refueling.GetProperty("volume").GetDecimal());
        Assert.Equal("EUR", refueling.GetProperty("currency").GetString()); // the instance default when none is given
        Assert.Equal(12000, refueling.GetProperty("odometer").GetInt64());

        var list = await Send("{ vehicles { name } }");
        Assert.Contains(list.GetProperty("data").GetProperty("vehicles").EnumerateArray(),
            v => v.GetProperty("name").GetString() == "Octavia");
    }

    [Fact]
    public async Task AddVehicle_WithBlankName_ReturnsValidationError()
    {
        var body = await Send(
            "mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }",
            new { i = new { name = " ", fuelType = "PETROL" } });

        var error = body.GetProperty("errors")[0];
        Assert.Equal("VALIDATION_FAILED", error.GetProperty("extensions").GetProperty("code").GetString());
        Assert.Equal("Vehicle name is required.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task LogRefueling_ForUnknownVehicle_ReturnsNotFound()
    {
        var body = await Send(
            "mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = Guid.NewGuid(), date = "2026-10-01", volume = 1, totalCost = 1, odometer = 1, isFullTank = true } });

        Assert.Equal("NOT_FOUND", body.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString());
    }
}

[Collection(ApiCollection.Name)]
public class VehicleLifecycleGraphQLTests(ApiFixture api)
{
    private async Task<JsonElement> Send(string query, object? variables = null)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> AddVehicle(string name)
    {
        var body = await Send("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name, fuelType = "PETROL" } });
        return body.GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString()!;
    }

    private static IEnumerable<string?> Names(JsonElement body, string field) =>
        body.GetProperty("data").GetProperty(field).EnumerateArray().Select(v => v.GetProperty("name").GetString());

    [Fact]
    public async Task Edit_Delete_Restore_AndEmptyTheTrash()
    {
        var id = await AddVehicle("Lifecycle car");

        var updated = await Send(
            "mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { name licensePlate fuelType canEdit owner { id } } }",
            new { i = new { id, name = "Renamed car", licensePlate = "ab-12", fuelType = "DIESEL" } });
        var vehicle = updated.GetProperty("data").GetProperty("updateVehicle");
        Assert.Equal("Renamed car", vehicle.GetProperty("name").GetString());
        Assert.Equal("AB-12", vehicle.GetProperty("licensePlate").GetString());
        Assert.Equal("DIESEL", vehicle.GetProperty("fuelType").GetString());
        Assert.True(vehicle.GetProperty("canEdit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, vehicle.GetProperty("owner").ValueKind); // no users when auth is off

        var deleted = await Send("mutation($id: UUID!) { deleteVehicle(id: $id) { deletedAt } }", new { id });
        Assert.NotEqual(JsonValueKind.Null, deleted.GetProperty("data").GetProperty("deleteVehicle").GetProperty("deletedAt").ValueKind);
        Assert.DoesNotContain("Renamed car", Names(await Send("{ vehicles { name } }"), "vehicles"));
        Assert.Contains("Renamed car", Names(await Send("{ trash { name } }"), "trash"));

        await Send("mutation($id: UUID!) { restoreVehicle(id: $id) { id } }", new { id });
        Assert.Contains("Renamed car", Names(await Send("{ vehicles { name } }"), "vehicles"));
        Assert.DoesNotContain("Renamed car", Names(await Send("{ trash { name } }"), "trash"));

        await Send("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id });
        var emptied = await Send("mutation { emptyTrash }");
        Assert.True(emptied.GetProperty("data").GetProperty("emptyTrash").GetInt32() >= 1);
        Assert.DoesNotContain("Renamed car", Names(await Send("{ trash { name } }"), "trash"));
        Assert.Equal(JsonValueKind.Null, (await Send("query($id: UUID!) { vehicle(id: $id) { id } }", new { id })).GetProperty("data").GetProperty("vehicle").ValueKind);
    }

    [Fact]
    public async Task EditingAMissingOrTrashedVehicle_IsNotFound()
    {
        var id = await AddVehicle("Soon trashed");
        await Send("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id });

        var edit = await Send("mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { id } }",
            new { i = new { id, name = "x", fuelType = "LPG" } });
        var restoreMissing = await Send("mutation($id: UUID!) { restoreVehicle(id: $id) { id } }", new { id = Guid.NewGuid() });

        Assert.Equal("NOT_FOUND", edit.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString());
        Assert.Equal("NOT_FOUND", restoreMissing.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateWithBlankName_IsAValidationError()
    {
        var id = await AddVehicle("Valid");

        var body = await Send("mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { id } }",
            new { i = new { id, name = " ", fuelType = "LPG" } });

        Assert.Equal("VALIDATION_FAILED", body.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString());
    }
}

[Collection(ApiCollection.Name)]
public class VehicleGridGraphQLTests(ApiFixture api)
{
    private async Task<JsonElement> Send(string query, object? variables = null)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task Add(string name, string fuel) =>
        await Send("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name, fuelType = fuel } });

    private const string Page = """
        query($orderBy: VehicleSortField!, $direction: SortDirection!, $skip: Int!, $take: Int!, $withFuel: Boolean!) {
          vehicles(orderBy: $orderBy, direction: $direction, skip: $skip, take: $take) { name fuelType @include(if: $withFuel) }
          vehicleCount
        }
        """;

    [Fact]
    public async Task Vehicles_AreSortedAndPagedByTheServer_WithAColumnSwitch()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        foreach (var (n, f) in new[] { ("Yy " + tag, "DIESEL"), ("Xx " + tag, "LPG"), ("Zz " + tag, "PETROL") }) await Add(n, f);

        var asc = await Send(Page, new { orderBy = "NAME", direction = "ASC", skip = 0, take = 200, withFuel = true });
        var ours = asc.GetProperty("data").GetProperty("vehicles").EnumerateArray().Where(v => v.GetProperty("name").GetString()!.EndsWith(tag)).ToList();
        Assert.Equal(["Xx " + tag, "Yy " + tag, "Zz " + tag], ours.Select(v => v.GetProperty("name").GetString()));
        Assert.Equal("LPG", ours[0].GetProperty("fuelType").GetString());
        var total = asc.GetProperty("data").GetProperty("vehicleCount").GetInt32();
        Assert.True(total >= 3);

        var desc = await Send(Page, new { orderBy = "NAME", direction = "DESC", skip = 0, take = 1, withFuel = false });
        var first = desc.GetProperty("data").GetProperty("vehicles")[0];
        Assert.Equal(1, desc.GetProperty("data").GetProperty("vehicles").GetArrayLength());
        Assert.False(first.TryGetProperty("fuelType", out _)); // switched off: not returned at all
        Assert.Equal(total, desc.GetProperty("data").GetProperty("vehicleCount").GetInt32()); // count ignores paging

        var beyond = await Send(Page, new { orderBy = "NAME", direction = "ASC", skip = total, take = 10, withFuel = true });
        Assert.Equal(0, beyond.GetProperty("data").GetProperty("vehicles").GetArrayLength());
    }

    [Fact]
    public async Task Defaults_ApplyWhenTheGridArgumentsAreOmitted()
    {
        await Add("Default order " + Guid.NewGuid().ToString("N")[..6], "PETROL");

        var body = await Send("{ vehicles { name } vehicleCount trash { id } trashCount }");

        Assert.False(body.TryGetProperty("errors", out _), body.ToString());
        Assert.True(body.GetProperty("data").GetProperty("vehicleCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task OutOfRangePaging_IsClamped()
    {
        var body = await Send("{ vehicles(skip: -10, take: 100000) { id } }");

        Assert.False(body.TryGetProperty("errors", out _), body.ToString());
    }

    [Fact]
    public async Task Trash_IsSortedByDeletionTime_NewestFirst_ByDefault()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var ids = new List<string>();
        foreach (var n in new[] { "First " + tag, "Second " + tag })
        {
            var added = await Send("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name = n, fuelType = "LPG" } });
            var id = added.GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString()!;
            ids.Add(id);
            await Send("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id });
            await Task.Delay(20);
        }

        var trash = await Send("{ trash { id name deletedAt } trashCount }");

        var names = trash.GetProperty("data").GetProperty("trash").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).Where(n => n.EndsWith(tag)).ToList();
        Assert.Equal(["Second " + tag, "First " + tag], names);
        Assert.True(trash.GetProperty("data").GetProperty("trashCount").GetInt32() >= 2);
    }

    [Fact]
    public async Task RefuelingCount_IsAvailablePerVehicle()
    {
        var added = await Send("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name = "Counted " + Guid.NewGuid().ToString("N")[..6], fuelType = "LPG" } });
        var id = added.GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString();
        await Send("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = id, date = "2026-10-01", volume = 1, totalCost = 1, odometer = 1, isFullTank = true } });

        var body = await Send("query($id: UUID!) { vehicle(id: $id) { refuelingCount } }", new { id });

        Assert.Equal(1, body.GetProperty("data").GetProperty("vehicle").GetProperty("refuelingCount").GetInt32());
    }

    [Fact]
    public async Task Errors_CarryATranslationKeyAndArguments()
    {
        var blank = await Send("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name = " ", fuelType = "LPG" } });
        var error = blank.GetProperty("errors")[0];
        var extensions = error.GetProperty("extensions");
        Assert.Equal("VALIDATION_FAILED", extensions.GetProperty("code").GetString());
        Assert.Equal("vehicle.nameRequired", extensions.GetProperty("key").GetString());
        Assert.Equal("Vehicle name is required.", error.GetProperty("message").GetString()); // English fallback

        var missing = await Send("mutation($id: UUID!) { restoreVehicle(id: $id) { id } }", new { id = Guid.NewGuid() });
        var notFound = missing.GetProperty("errors")[0].GetProperty("extensions");
        Assert.Equal("NOT_FOUND", notFound.GetProperty("code").GetString());
        Assert.Equal("vehicle.notFound", notFound.GetProperty("key").GetString());
        Assert.True(notFound.GetProperty("args").TryGetProperty("id", out _));
    }
}

