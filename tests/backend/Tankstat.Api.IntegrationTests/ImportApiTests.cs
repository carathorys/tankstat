using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Importing a Fuelio file end to end: raw upload over HTTP, preview and confirmation through GraphQL, and expenses.</summary>
[Collection(ApiCollection.Name)]
public class ImportApiTests : IDisposable
{
    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private const string Sample = """
        "## Vehicle"
        "Name","DistUnit","FuelUnit","Plate","Tank1Type"
        "Polo","0","0","abc-123","100"
        "## Log"
        "Data","Odo (km)","Fuel (litres)","Full","Price (optional)","Notes (optional)"
        "2026-09-17 18:15","1300.0","38.5","1","24669.0","Trip"
        "2026-07-11 18:37","1000.0","40.0","0","22329.0",""
        "## CostCategories"
        "CostTypeID","Name"
        "1","Service"
        "## Costs"
        "CostTitle","Date","Odo","CostTypeID","Notes","Cost","isTemplate","isIncome"
        "Oil change","2026-07-20 09:00","1100","1","","35000.0","0","0"
        """;

    private static async Task<HttpResponseMessage> Upload(HttpClient c, string format, string body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return await c.PostAsync($"/imports/{format}", content);
    }

    private async Task<HttpClient> Alice()
    {
        var admin = _app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }", new { i = new { email = "alice@example.com", displayName = "alice", isAdmin = false } })).Data().GetProperty("createUser");
        await _app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }", new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = "alice-password-1" } });
        var client = _app.NewClient();
        await client.LoginAs("alice@example.com", "alice-password-1");
        return client;
    }

    private static async Task<string> Token(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<string?> ErrorKey(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString();

    [Fact]
    public async Task UploadPreviewAndConfirm_CreateTheVehicleWithItsLogsAndExpenses()
    {
        var alice = await Alice();
        var token = await Token(await Upload(alice, "fuelio", Sample));

        var preview = (await alice.Gql("query($t: String!) { importPreview(token: $t) { fuelRows expenseRows categories firstDate lastDate sourceVehicle { name licensePlate fuelType distanceUnit volumeUnit } issues { key } } }", new { t = token })).Data().GetProperty("importPreview");
        Assert.Equal((2, 1), (preview.GetProperty("fuelRows").GetInt32(), preview.GetProperty("expenseRows").GetInt32()));
        Assert.Equal("Service", preview.GetProperty("categories")[0].GetString());
        Assert.Equal(("2026-07-11", "2026-09-17"), (preview.GetProperty("firstDate").GetString(), preview.GetProperty("lastDate").GetString()));
        Assert.Equal(("Polo", "PETROL", "KILOMETERS"), (preview.GetProperty("sourceVehicle").GetProperty("name").GetString(), preview.GetProperty("sourceVehicle").GetProperty("fuelType").GetString(), preview.GetProperty("sourceVehicle").GetProperty("distanceUnit").GetString()));

        var result = (await alice.Gql(
            "mutation($i: ConfirmImportInput!) { confirmImport(input: $i) { vehicleId fuelImported expensesImported errors { key } } }",
            new { i = new { token, newVehicle = new { name = "Polo", licensePlate = "abc-123", fuelType = "PETROL", units = new { distance = "KILOMETERS", volume = "LITERS" } }, currency = "huf", importDuplicates = false } })).Data().GetProperty("confirmImport");
        Assert.Equal((2, 1, 0), (result.GetProperty("fuelImported").GetInt32(), result.GetProperty("expensesImported").GetInt32(), result.GetProperty("errors").GetArrayLength()));

        var id = result.GetProperty("vehicleId").GetString();
        var read = (await alice.Gql("query($id: UUID!) { refuelings(vehicleId: $id) { currency totalCost odometer isFullTank note } expenses(vehicleId: $id) { title category amount currency odometer } expenseCount(vehicleId: $id) }", new { id })).Data();
        Assert.Equal(("HUF", 24669m, 1300L, "Trip"), (read.GetProperty("refuelings")[0].GetProperty("currency").GetString(), read.GetProperty("refuelings")[0].GetProperty("totalCost").GetDecimal(), read.GetProperty("refuelings")[0].GetProperty("odometer").GetInt64(), read.GetProperty("refuelings")[0].GetProperty("note").GetString()));
        var expense = read.GetProperty("expenses")[0];
        Assert.Equal(("Oil change", "Service", 35000m, 1100L), (expense.GetProperty("title").GetString(), expense.GetProperty("category").GetString(), expense.GetProperty("amount").GetDecimal(), expense.GetProperty("odometer").GetInt64()));
        Assert.Equal(1, read.GetProperty("expenseCount").GetInt32());
    }

    [Fact]
    public async Task ImportingAgain_IntoTheSameVehicle_ReportsAndSkipsDuplicates()
    {
        var alice = await Alice();
        var first = await Token(await Upload(alice, "fuelio", Sample));
        var id = (await alice.Gql("mutation($i: ConfirmImportInput!) { confirmImport(input: $i) { vehicleId } }", new { i = new { token = first, newVehicle = new { name = "Polo", fuelType = "PETROL", units = new { distance = "KILOMETERS", volume = "LITERS" } }, importDuplicates = false } })).Data().GetProperty("confirmImport").GetProperty("vehicleId").GetString();

        var second = await Token(await Upload(alice, "fuelio", Sample));
        var preview = (await alice.Gql("query($t: String!, $v: UUID) { importPreview(token: $t, vehicleId: $v) { duplicateFuelRows duplicateExpenseRows } }", new { t = second, v = id })).Data().GetProperty("importPreview");
        Assert.Equal((2, 1), (preview.GetProperty("duplicateFuelRows").GetInt32(), preview.GetProperty("duplicateExpenseRows").GetInt32()));

        var result = (await alice.Gql("mutation($i: ConfirmImportInput!) { confirmImport(input: $i) { fuelImported fuelSkippedDuplicates expensesSkippedDuplicates } }", new { i = new { token = second, vehicleId = id, importDuplicates = false } })).Data().GetProperty("confirmImport");
        Assert.Equal((0, 2, 1), (result.GetProperty("fuelImported").GetInt32(), result.GetProperty("fuelSkippedDuplicates").GetInt32(), result.GetProperty("expensesSkippedDuplicates").GetInt32()));
    }

    [Fact]
    public async Task BadUploads_AreRejectedWithTranslatableKeys()
    {
        var alice = await Alice();

        var unknown = await Upload(alice, "excel", Sample);
        var notFuelio = await Upload(alice, "fuelio", "a,b,c\n1,2,3\n");
        var empty = await Upload(alice, "fuelio", "\"## Log\"\n\"Data\",\"Odo (km)\",\"Fuel (litres)\",\"Price (optional)\"\n");
        var big = await Upload(alice, "fuelio", new string('x', 5 * 1024 * 1024 + 1));

        Assert.Equal((HttpStatusCode.BadRequest, "import.unknownFormat"), (unknown.StatusCode, await ErrorKey(unknown)));
        Assert.Equal((HttpStatusCode.BadRequest, "import.unreadable"), (notFuelio.StatusCode, await ErrorKey(notFuelio)));
        Assert.Equal((HttpStatusCode.BadRequest, "import.nothingFound"), (empty.StatusCode, await ErrorKey(empty)));
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "import.tooLarge"), (big.StatusCode, await ErrorKey(big)));
    }

    [Fact]
    public async Task OnlySignedInUsers_CanUpload_AndTokensBelongToTheUploader()
    {
        var alice = await Alice();
        var token = await Token(await Upload(alice, "fuelio", Sample));

        var anonymous = await Upload(_app.NewClient(), "fuelio", Sample);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var admin = _app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        var stolen = await admin.Gql("query($t: String!) { importPreview(token: $t) { fuelRows } }", new { t = token });
        Assert.Equal("import.expired", stolen.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
    }

    [Fact]
    public async Task Formats_AreListed()
    {
        var alice = await Alice();

        var formats = (await alice.Gql("{ importFormats }")).Data().GetProperty("importFormats");

        Assert.Equal("fuelio", formats[0].GetString());
    }

    [Fact]
    public async Task Expenses_CanBeAddedEditedTrashedRestoredAndPurged()
    {
        var alice = await Alice();
        var car = (await alice.Gql("mutation { addVehicle(input: { name: \"Car\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString();

        var added = (await alice.Gql("mutation($i: AddExpenseInput!) { addExpense(input: $i) { id title category amount currency odometer canEdit canDelete } }", new { i = new { vehicleId = car, date = "2026-09-01", title = "Wash", category = "Care", amount = 12.5, currency = "EUR", odometer = 500 } })).Data().GetProperty("addExpense");
        Assert.Equal(("Wash", "Care", 12.5m, 500L, true, true), (added.GetProperty("title").GetString(), added.GetProperty("category").GetString(), added.GetProperty("amount").GetDecimal(), added.GetProperty("odometer").GetInt64(), added.GetProperty("canEdit").GetBoolean(), added.GetProperty("canDelete").GetBoolean()));
        var id = added.GetProperty("id").GetString();

        var updated = (await alice.Gql("mutation($i: UpdateExpenseInput!) { updateExpense(input: $i) { title odometer } }", new { i = new { id, date = "2026-09-01", title = "Car wash", amount = 15, odometer = (long?)null } })).Data().GetProperty("updateExpense");
        Assert.Equal(("Car wash", JsonValueKind.Null), (updated.GetProperty("title").GetString(), updated.GetProperty("odometer").ValueKind));

        await alice.Gql("mutation($id: UUID!) { deleteExpense(id: $id) { id } }", new { id });
        var trash = (await alice.Gql("{ expenseTrash { title vehicle { name } } expenseTrashCount expenseTrashDeletableCount }")).Data();
        Assert.Equal(("Car wash", "Car", 1, 1), (trash.GetProperty("expenseTrash")[0].GetProperty("title").GetString(), trash.GetProperty("expenseTrash")[0].GetProperty("vehicle").GetProperty("name").GetString(), trash.GetProperty("expenseTrashCount").GetInt32(), trash.GetProperty("expenseTrashDeletableCount").GetInt32()));

        await alice.Gql("mutation($id: UUID!) { restoreExpense(id: $id) { id } }", new { id });
        await alice.Gql("mutation($id: UUID!) { deleteExpense(id: $id) { id } }", new { id });
        Assert.Equal(1, (await alice.Gql("mutation { emptyExpenseTrash }")).Data().GetProperty("emptyExpenseTrash").GetInt32());
    }
}
