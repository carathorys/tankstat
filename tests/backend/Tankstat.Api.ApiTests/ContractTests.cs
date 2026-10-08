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
    public async Task PhotoReading_IsOffUnlessSetUp_AndUnknownDraftsAreLeftOut()
    {
        var body = await Query("{ recognitionStatus { available } photoDrafts(ids: [\"00000000-0000-0000-0000-000000000001\"]) { id } }");

        Assert.False(body.TryGetProperty("errors", out _), body.ToString());
        var data = body.GetProperty("data");
        Assert.False(data.GetProperty("recognitionStatus").GetProperty("available").GetBoolean());
        Assert.Empty(data.GetProperty("photoDrafts").EnumerateArray());
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
    [InlineData("Query", "health", "vehicles", "myVehicles", "myVehicleCount", "vehicle", "session", "notices", "users", "canSetUserPasswords", "accessSettings", "accessGrants", "trash", "vehicleCount", "trashCount", "trashDeletableCount", "vehicleDefaults", "refuelings", "refuelingCount", "refueling", "logDefaults", "refuelingTrash", "refuelingTrashCount", "refuelingTrashDeletableCount", "vehicleLogAccess", "shareCandidates", "notifications", "notificationCount", "recognitionStatus", "photoDrafts", "uiSettings", "mySessions", "offlineChanges", "offlineSettings")]
    [InlineData("Mutation", "addVehicle", "logRefueling", "login", "logout", "changePassword", "requestPasswordReset", "resetPassword", "createUser", "issuePasswordReset", "setUserAdmin", "setUserDisabled", "updateUser", "setUserPassword", "deleteUser", "setDefaultAccess", "setAccessGrant", "updateVehicle", "deleteVehicle", "restoreVehicle", "emptyTrash", "logRefueling", "updateRefueling", "deleteRefueling", "restoreRefueling", "emptyRefuelingTrash", "setVehicleLogAccess", "markNotificationsRead", "updateUiSettings", "saveGridSettings", "resetGridSettings", "setVehicleOrder", "addRecurringExpense", "updateRecurringExpense", "deleteRecurringExpense", "markRecurringExpensesDone", "revokeSession", "revokeOtherSessions", "updateOfflineSettings", "syncChanges")]
    [InlineData("Session", "mode", "user")]
    [InlineData("UserInfo", "id", "displayName", "email", "isAdmin")]
    [InlineData("UserSessionInfo", "id", "client", "createdAt", "lastUsedAt", "expiresAt", "current")]
    [InlineData("UserAccount", "id", "provider", "email", "displayName", "isAdmin", "isDisabled")]
    [InlineData("Notice", "code", "severity", "message")]
    [InlineData("Vehicle", "id", "ownerId", "name", "licensePlate", "fuelType", "units", "deletedAt", "canEdit", "logAccess", "owner", "refuelingCount", "version", "updatedAt", "logCountSince")]
    [InlineData("MeasurementUnits", "distance", "volume")]
    [InlineData("UserRef", "id", "displayName", "avatarUrl")]
    [InlineData("LogDefaults", "lastOdometer", "lastDate", "currency")]
    [InlineData("VehicleDefaults", "distanceUnit", "volumeUnit", "currency", "recurringWarnDays", "recurringWarnDistance")]
    [InlineData("LogAccessGrantInfo", "user", "level")]
    [InlineData("Expense", "id", "vehicleId", "createdBy", "date", "title", "category", "amount", "currency", "odometer", "note", "deletedAt", "canEdit", "canDelete", "vehicle", "photos", "schedules", "version", "updatedAt")]
    [InlineData("MarkRecurringExpensesDonePayload", "schedules", "expense")]
    [InlineData("RecurringExpenseInfo", "id", "vehicleId", "title", "createdAt", "status", "version", "updatedAt")]
    [InlineData("RecurringRef", "id", "title")]
    [InlineData("OfflineChanges", "vehicle", "refuelings", "expenses", "recurring", "removed", "next", "watermark", "resync")]
    [InlineData("RemovedEntity", "type", "id")]
    [InlineData("OfflineSettingsInfo", "defaultWindow", "vehicles")]
    [InlineData("OfflineVehicleWindow", "vehicleId", "window")]
    [InlineData("LogCountSince", "refuelings", "expenses")]
    [InlineData("SyncResultInfo", "results", "applied", "parked")]
    [InlineData("SyncChangeResultInfo", "id", "status", "entityId", "version", "reason")]
    [InlineData("SyncReasonInfo", "key", "args")]
    [InlineData("SyncReasonArg", "name", "value")]
    [InlineData("LogPhotoInfo", "id", "url", "reading")]
    [InlineData("RecognitionStatusInfo", "available")]
    [InlineData("PhotoDraftInfo", "id", "url", "reading")]
    [InlineData("PhotoReadingInfo", "status", "kind", "values")]
    [InlineData("ReadingValueInfo", "name", "value", "confidence")]
    [InlineData("NotificationInfo", "id", "kind", "createdAt", "updatedAt", "read", "readAt", "count", "subject", "context", "args")]
    [InlineData("NotificationRefInfo", "type", "id")]
    [InlineData("NotificationArg", "name", "value")]
    [InlineData("ImportPreviewInfo", "sourceVehicle", "fuelRows", "expenseRows", "recurringRows", "duplicateFuelRows", "duplicateExpenseRows", "duplicateRecurringRows", "firstDate", "lastDate", "categories", "issues")]
    [InlineData("ImportResultInfo", "vehicleId", "fuelImported", "expensesImported", "recurringImported", "fuelSkippedDuplicates", "expensesSkippedDuplicates", "recurringSkippedDuplicates", "errors")]
    [InlineData("ImportIssueInfo", "section", "row", "key", "args")]
    [InlineData("VehicleSummary", "lastFillUpDate", "latestOdometer", "averageConsumption", "currency", "thisMonthSpend", "lastMonthSpend", "spendTrend", "fillUpCount", "expenseCount", "spending")]
    [InlineData("CurrencySpend", "currency", "thisMonth", "lastMonth")]
    [InlineData("ChartData", "unit", "series")]
    [InlineData("ChartSeries", "kind", "currency", "points")]
    [InlineData("VehicleChart", "id", "vehicleId", "title", "metric", "grouping", "kind", "range", "rangeFrom", "rangeTo", "stacked", "isShared", "createdAt", "canEdit", "createdBy")]
    [InlineData("Refueling", "id", "vehicleId", "createdBy", "date", "volume", "totalCost", "currency", "odometer", "pricePerUnit", "consumption", "isFullTank", "missedPreviousFillUp", "note", "deletedAt", "canEdit", "canDelete", "vehicle", "photos", "reviewState", "filledFromPhoto", "version", "updatedAt")]
    [InlineData("Expense", "id", "amount", "currency", "odometer", "reviewState", "filledFromPhoto")]
    [InlineData("UiSettingsInfo", "navOpen", "language", "colorMode", "grids")]
    [InlineData("GridSettingsInfo", "gridId", "order", "hidden", "pageSize", "sortColumn", "sortDirection")]
    public async Task Schema_TypeExposesContractFields(string type, params string[] fields)
    {
        var names = await FieldNames(type);

        foreach (var field in fields) Assert.Contains(field, names);
    }

    [Theory]
    [InlineData("LogRefuelingInput", "vehicleId", "date", "volume", "totalCost", "currency", "odometer", "isFullTank", "note", "photoIds", "missedPreviousFillUp", "id")]
    [InlineData("UpdateRefuelingInput", "id", "date", "volume", "totalCost", "currency", "odometer", "isFullTank", "note", "missedPreviousFillUp")]
    [InlineData("AddExpenseInput", "vehicleId", "date", "title", "category", "amount", "currency", "odometer", "note", "photoIds", "id")]
    [InlineData("AddVehicleInput", "name", "licensePlate", "fuelType", "units", "id")]
    [InlineData("AddRecurringExpenseInput", "vehicleId", "title", "kind", "intervalMonths", "intervalDistance", "lastDoneDate", "lastDoneOdometer", "id")]
    [InlineData("MarkRecurringExpensesDoneInput", "ids", "date", "odometer", "amount", "currency", "title", "category", "photoIds", "expenseId")]
    [InlineData("GridSettingsInput", "gridId", "order", "hidden", "pageSize", "sortColumn", "sortDirection")]
    [InlineData("UpdateUiSettingsInput", "navOpen", "language", "clearLanguage", "colorMode")]
    public async Task Schema_InputTypeTakesContractFields(string type, params string[] fields)
    {
        var body = await Query($"{{ __type(name: \"{type}\") {{ inputFields {{ name }} }} }}");

        var names = body.GetProperty("data").GetProperty("__type").GetProperty("inputFields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        foreach (var field in fields) Assert.Contains(field, names);
    }

    [Fact]
    public async Task Schema_LogValuesThatPhotosFillIn_AreOptional_AndTheReviewStatesAreKnown()
    {
        var input = await Query("{ __type(name: \"LogRefuelingInput\") { inputFields { name type { kind } } } }");
        var kinds = input.GetProperty("data").GetProperty("__type").GetProperty("inputFields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("type").GetProperty("kind").GetString());
        var states = (await Query("{ __type(name: \"ReviewState\") { enumValues { name } } }"))
            .GetProperty("data").GetProperty("__type").GetProperty("enumValues").EnumerateArray().Select(v => v.GetProperty("name").GetString());

        Assert.Equal(("SCALAR", "SCALAR", "SCALAR", "NON_NULL"), (kinds["volume"], kinds["totalCost"], kinds["odometer"], kinds["date"]));
        Assert.Equal(["NONE", "AWAITING_PHOTOS", "NEEDS_REVIEW", "INCOMPLETE"], states);
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
    [InlineData("ColorMode", "LIGHT", "DARK", "SYSTEM")]
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
    [InlineData("NotificationKind", "LOG_ACCESS_CHANGED", "VEHICLE_SHARED", "DATA_ACCESS_CHANGED", "DATA_SHARED", "DEFAULT_ACCESS_CHANGED", "RECURRING_DUE_SOON", "RECURRING_OVERDUE", "LOG_FILLED_FROM_PHOTO", "LOG_NOT_FILLED", "MORE_ACTIVITY")]
    [InlineData("NotificationEntityType", "INSTANCE", "VEHICLE", "RECURRING_EXPENSE", "USER", "REFUELING", "EXPENSE")]
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
    public async Task UiSettings_AndVehicleOrder_RoundTrip_AgainstRealDatabase()
    {
        var gridId = $"contract-{Guid.NewGuid():N}"[..20];
        var saved = await Query($"mutation {{ saveGridSettings(input: {{ gridId: \"{gridId}\", order: [\"name\", \"owner\"], hidden: [\"owner\"], pageSize: 10, sortColumn: \"name\", sortDirection: DESC }}) {{ gridId pageSize sortDirection }} }}");
        Assert.False(saved.TryGetProperty("errors", out _), saved.ToString());

        var read = await Query("{ uiSettings { grids { gridId hidden pageSize } } }");
        var grid = read.GetProperty("data").GetProperty("uiSettings").GetProperty("grids").EnumerateArray().Single(g => g.GetProperty("gridId").GetString() == gridId);
        Assert.Equal(10, grid.GetProperty("pageSize").GetInt32());
        Assert.Equal(["owner"], grid.GetProperty("hidden").EnumerateArray().Select(h => h.GetString()));
        Assert.True((await Query($"mutation {{ resetGridSettings(gridId: \"{gridId}\") }}")).GetProperty("data").GetProperty("resetGridSettings").GetBoolean());

        var tag = Guid.NewGuid().ToString("N")[..6];
        var first = (await Query($"mutation {{ addVehicle(input: {{ name: \"Order A {tag}\", fuelType: PETROL }}) {{ id }} }}")).GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString();
        var second = (await Query($"mutation {{ addVehicle(input: {{ name: \"Order B {tag}\", fuelType: PETROL }}) {{ id }} }}")).GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString();
        var arranged = await Query($"mutation {{ setVehicleOrder(vehicleIds: [\"{second}\", \"{first}\"]) }}");
        Assert.False(arranged.TryGetProperty("errors", out _), arranged.ToString());

        var names = (await Query($"{{ myVehicles(search: \"{tag}\") {{ name }} }}")).GetProperty("data").GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()).ToArray();
        Assert.Equal([$"Order B {tag}", $"Order A {tag}"], names);
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
