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
            new { i = new { vehicleId = id, date = "2026-10-01", liters = 41.5, totalCost = 79.9, odometerKm = 12000, isFullTank = true } });
        Assert.False(logged.TryGetProperty("errors", out _), logged.ToString());

        var read = await Send(
            "query($id: UUID!) { vehicle(id: $id) { name refuelings { date liters totalCost odometerKm isFullTank } } }",
            new { id });
        Assert.False(read.TryGetProperty("errors", out _), read.ToString());
        var refueling = read.GetProperty("data").GetProperty("vehicle").GetProperty("refuelings")[0];
        Assert.Equal("2026-10-01", refueling.GetProperty("date").GetString());
        Assert.Equal(41.5m, refueling.GetProperty("liters").GetDecimal());
        Assert.Equal(12000, refueling.GetProperty("odometerKm").GetInt32());

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
            new { i = new { vehicleId = Guid.NewGuid(), date = "2026-10-01", liters = 1, totalCost = 1, odometerKm = 1, isFullTank = true } });

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
            "mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { name licensePlate fuelType canEdit ownerName } }",
            new { i = new { id, name = "Renamed car", licensePlate = "ab-12", fuelType = "DIESEL" } });
        var vehicle = updated.GetProperty("data").GetProperty("updateVehicle");
        Assert.Equal("Renamed car", vehicle.GetProperty("name").GetString());
        Assert.Equal("AB-12", vehicle.GetProperty("licensePlate").GetString());
        Assert.Equal("DIESEL", vehicle.GetProperty("fuelType").GetString());
        Assert.True(vehicle.GetProperty("canEdit").GetBoolean());
        Assert.Equal(JsonValueKind.Null, vehicle.GetProperty("ownerName").ValueKind); // no users when auth is off

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
