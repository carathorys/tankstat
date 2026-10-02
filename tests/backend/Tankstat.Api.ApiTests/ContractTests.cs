using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.ApiTests;

/// <summary>
/// Black-box contract tests against a running server (TANKSTAT_API_URL, default http://localhost:5080).
/// They pin the GraphQL schema shape the frontend depends on. Run via `mise run test:api`.
/// </summary>
public class ContractTests
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("TANKSTAT_API_URL") ?? "http://localhost:5080"),
    };

    private static async Task<JsonElement> Query(string query)
    {
        var response = await Http.PostAsJsonAsync("/graphql", new { query });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Health_ExposesContractFields()
    {
        var body = await Query("{ health { status version uptime databaseReachable } }");

        Assert.False(body.TryGetProperty("errors", out _), body.ToString());
        var health = body.GetProperty("data").GetProperty("health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, health.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.String, health.GetProperty("uptime").ValueKind);
        Assert.Equal(JsonValueKind.True, health.GetProperty("databaseReachable").ValueKind);
    }

    private static async Task<List<string?>> FieldNames(string type)
    {
        var body = await Query($"{{ __type(name: \"{type}\") {{ fields {{ name }} }} }}");
        return body.GetProperty("data").GetProperty("__type").GetProperty("fields")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
    }

    [Theory]
    [InlineData("Query", "health", "vehicles", "vehicle", "session", "notices", "users", "accessSettings", "accessGrants", "trash", "vehicleCount", "trashCount", "trashDeletableCount", "vehicleDefaults", "refuelings", "refuelingCount", "refueling", "logDefaults", "refuelingTrash", "refuelingTrashCount", "refuelingTrashDeletableCount", "vehicleLogAccess", "shareCandidates")]
    [InlineData("Mutation", "addVehicle", "logRefueling", "login", "logout", "changePassword", "requestPasswordReset", "resetPassword", "createUser", "issuePasswordReset", "setUserAdmin", "setUserDisabled", "setDefaultAccess", "setAccessGrant", "updateVehicle", "deleteVehicle", "restoreVehicle", "emptyTrash", "logRefueling", "updateRefueling", "deleteRefueling", "restoreRefueling", "emptyRefuelingTrash", "setVehicleLogAccess")]
    [InlineData("Session", "mode", "user")]
    [InlineData("UserInfo", "id", "displayName", "email", "isAdmin")]
    [InlineData("UserAccount", "id", "provider", "email", "displayName", "isAdmin", "isDisabled")]
    [InlineData("Notice", "code", "severity", "message")]
    [InlineData("Vehicle", "id", "ownerId", "name", "licensePlate", "fuelType", "units", "deletedAt", "canEdit", "logAccess", "owner", "refuelingCount")]
    [InlineData("MeasurementUnits", "distance", "volume")]
    [InlineData("UserRef", "id", "displayName", "avatarUrl")]
    [InlineData("LogDefaults", "lastOdometer", "lastDate", "currency")]
    [InlineData("LogAccessGrantInfo", "user", "level")]
    [InlineData("Expense", "id", "vehicleId", "createdBy", "date", "title", "category", "amount", "currency", "odometer", "note", "deletedAt", "canEdit", "canDelete", "vehicle", "photos")]
    [InlineData("LogPhotoInfo", "id", "url")]
    [InlineData("ImportPreviewInfo", "sourceVehicle", "fuelRows", "expenseRows", "duplicateFuelRows", "duplicateExpenseRows", "firstDate", "lastDate", "categories", "issues")]
    [InlineData("ImportResultInfo", "vehicleId", "fuelImported", "expensesImported", "fuelSkippedDuplicates", "expensesSkippedDuplicates", "errors")]
    [InlineData("ImportIssueInfo", "section", "row", "key", "args")]
    [InlineData("VehicleSummary", "lastFillUpDate", "latestOdometer", "averageConsumption", "currency", "thisMonthSpend", "lastMonthSpend", "spendTrend", "fillUpCount", "expenseCount")]
    [InlineData("ChartData", "unit", "series")]
    [InlineData("ChartSeries", "kind", "currency", "points")]
    [InlineData("VehicleChart", "id", "vehicleId", "title", "metric", "grouping", "kind", "range", "rangeFrom", "rangeTo", "stacked", "isShared", "createdAt", "canEdit", "createdBy")]
    [InlineData("Refueling", "id", "vehicleId", "createdBy", "date", "volume", "totalCost", "currency", "odometer", "pricePerUnit", "consumption", "isFullTank", "note", "deletedAt", "canEdit", "canDelete", "vehicle", "photos")]
    public async Task Schema_TypeExposesContractFields(string type, params string[] fields)
    {
        var names = await FieldNames(type);

        foreach (var field in fields) Assert.Contains(field, names);
    }

    [Fact]
    public async Task Schema_FuelTypeEnumValues()
    {
        var body = await Query("{ __type(name: \"FuelType\") { enumValues { name } } }");

        var values = body.GetProperty("data").GetProperty("__type").GetProperty("enumValues")
            .EnumerateArray().Select(v => v.GetProperty("name").GetString());
        Assert.Equal(["PETROL", "DIESEL", "LPG"], values);
    }

    [Theory]
    [InlineData("VehicleSortField", "NAME", "LICENSE_PLATE", "FUEL_TYPE", "OWNER", "REFUELING_COUNT", "DELETED_AT")]
    [InlineData("SortDirection", "ASC", "DESC")]
    [InlineData("AuthMode", "NONE", "STANDALONE", "OIDC", "PROXY_HEADER")]
    [InlineData("AccessLevel", "NONE", "VIEW", "EDIT", "DELETE")]
    [InlineData("DistanceUnit", "KILOMETERS", "MILES")]
    [InlineData("VolumeUnit", "LITERS", "US_GALLONS", "IMPERIAL_GALLONS")]
    [InlineData("RefuelingSortField", "DATE", "VOLUME", "TOTAL_COST", "ODOMETER", "PRICE_PER_UNIT", "CONSUMPTION", "CREATED_BY", "VEHICLE", "DELETED_AT")]
    [InlineData("ExpenseSortField", "DATE", "TITLE", "CATEGORY", "AMOUNT", "ODOMETER", "CREATED_BY", "VEHICLE", "DELETED_AT")]
    [InlineData("ChartMetric", "TOTAL_SPEND", "FUEL_COST", "EXPENSE_COST", "FUEL_VOLUME", "DISTANCE", "AVERAGE_CONSUMPTION", "AVERAGE_PRICE_PER_UNIT", "FILL_UPS")]
    [InlineData("ChartGrouping", "MONTH", "QUARTER", "YEAR", "CATEGORY")]
    [InlineData("ChartKind", "BAR", "LINE", "AREA", "DONUT")]
    [InlineData("ChartRange", "LAST1_MONTH", "LAST3_MONTHS", "LAST6_MONTHS", "LAST12_MONTHS", "THIS_YEAR", "LAST_YEAR", "ALL", "CUSTOM")]
    [InlineData("NoticeSeverity", "INFO", "WARNING")]
    public async Task Schema_EnumsExposeContractValues(string type, params string[] expected)
    {
        var body = await Query($"{{ __type(name: \"{type}\") {{ enumValues {{ name }} }} }}");

        var values = body.GetProperty("data").GetProperty("__type").GetProperty("enumValues")
            .EnumerateArray().Select(v => v.GetProperty("name").GetString());
        Assert.Equal(expected, values);
    }

    [Theory]
    [InlineData("vehicles")]
    [InlineData("trash")]
    public async Task Lists_TakeSortingAndPagingArguments(string field)
    {
        var body = await Query("{ __type(name: \"Query\") { fields { name args { name } } } }");

        var args = body.GetProperty("data").GetProperty("__type").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == field).GetProperty("args").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString());
        Assert.Equal(["orderBy", "direction", "skip", "take"], args);
    }

    [Fact]
    public async Task Session_AndNotices_ArePublic()
    {
        var body = await Query("{ session { mode } notices { code severity message } }");

        Assert.False(body.TryGetProperty("errors", out _), body.ToString());
        Assert.Equal("NONE", body.GetProperty("data").GetProperty("session").GetProperty("mode").GetString());
        Assert.Equal("AUTH_DISABLED", body.GetProperty("data").GetProperty("notices")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Schema_NeverExposesPasswordData()
    {
        var body = (await Query("{ __schema { types { name fields { name } } } }")).ToString().ToLowerInvariant();

        Assert.DoesNotContain("passwordhash", body);
        Assert.DoesNotContain("secrethash", body);
    }

    [Fact]
    public async Task Vehicles_EndToEnd_AgainstRealDatabase()
    {
        var added = await Query("mutation { addVehicle(input: { name: \"Contract car\", fuelType: PETROL }) { id } }");
        var id = added.GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString();
        await Query($"mutation {{ logRefueling(input: {{ vehicleId: \"{id}\", date: \"2026-10-01\", volume: 10, totalCost: 20, currency: \"HUF\", odometer: 100, isFullTank: true }}) {{ id }} }}");

        var read = await Query($"{{ vehicle(id: \"{id}\") {{ name }} refuelings(vehicleId: \"{id}\") {{ volume currency }} refuelingCount(vehicleId: \"{id}\") }}");

        Assert.False(read.TryGetProperty("errors", out _), read.ToString());
        Assert.Equal("HUF", Assert.Single(read.GetProperty("data").GetProperty("refuelings").EnumerateArray()).GetProperty("currency").GetString());
        Assert.Equal(1, read.GetProperty("data").GetProperty("refuelingCount").GetInt32());
    }

    [Fact]
    public async Task Vehicles_SoftDeleteRestoreAndEmptyTrash_AgainstRealDatabase()
    {
        var added = await Query("mutation { addVehicle(input: { name: \"Trash car\", fuelType: DIESEL }) { id } }");
        var id = added.GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString();

        var deleted = await Query($"mutation {{ deleteVehicle(id: \"{id}\") {{ deletedAt }} }}");
        Assert.NotEqual(JsonValueKind.Null, deleted.GetProperty("data").GetProperty("deleteVehicle").GetProperty("deletedAt").ValueKind);
        var trash = await Query("{ trash { id } }");
        Assert.Contains(trash.GetProperty("data").GetProperty("trash").EnumerateArray(), v => v.GetProperty("id").GetString() == id);

        await Query($"mutation {{ restoreVehicle(id: \"{id}\") {{ deletedAt }} }}");
        await Query($"mutation {{ deleteVehicle(id: \"{id}\") {{ id }} }}");
        var emptied = await Query("mutation { emptyTrash }");

        Assert.True(emptied.GetProperty("data").GetProperty("emptyTrash").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.Null, (await Query($"{{ vehicle(id: \"{id}\") {{ id }} }}")).GetProperty("data").GetProperty("vehicle").ValueKind);
    }
}
