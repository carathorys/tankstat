using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RecurringGraphQLTests(ApiFixture api)
{
    private async Task<JsonElement> Send(string query, object? variables = null)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> AddVehicle()
    {
        var added = await Send("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name = "Recurring " + Guid.NewGuid().ToString("N")[..6], fuelType = "PETROL" } });
        return added.GetProperty("data").GetProperty("addVehicle").GetProperty("id").GetString()!;
    }

    private const string Fields = "id title kind intervalMonths intervalDistance lastDoneDate lastDoneOdometer warnDays warnDistance status { state limit dueDate dueOdometer daysLeft distanceLeft }";

    private async Task<JsonElement> AddItem(string vehicleId, object? over = null)
    {
        var input = new Dictionary<string, object?>
        {
            ["vehicleId"] = vehicleId, ["title"] = "Oil change", ["category"] = "Service", ["kind"] = "COMBINED", ["intervalMonths"] = 12,
            ["intervalDistance"] = 15000, ["lastDoneDate"] = "2026-01-15", ["lastDoneOdometer"] = 50000,
        };
        if (over is not null) foreach (var p in over.GetType().GetProperties()) input[p.Name] = p.GetValue(over);
        var body = await Send($"mutation($i: AddRecurringExpenseInput!) {{ addRecurringExpense(input: $i) {{ {Fields} }} }}", new { i = input });
        return body;
    }

    [Fact]
    public async Task ASchedule_IsAdded_ListedOnTheVehicle_WithItsStatus()
    {
        var vehicle = await AddVehicle();

        var added = (await AddItem(vehicle)).GetProperty("data").GetProperty("addRecurringExpense");

        Assert.Equal(("COMBINED", 30, 500), (added.GetProperty("kind").GetString(), added.GetProperty("warnDays").GetInt32(), (int)added.GetProperty("warnDistance").GetInt64()));
        Assert.Equal("2027-01-15", added.GetProperty("status").GetProperty("dueDate").GetString());
        Assert.Equal(65000, added.GetProperty("status").GetProperty("dueOdometer").GetInt64());

        var listed = (await Send($"query($id: UUID!) {{ vehicle(id: $id) {{ recurring {{ {Fields} }} }} }}", new { id = vehicle })).GetProperty("data").GetProperty("vehicle").GetProperty("recurring");
        Assert.Equal(["Oil change"], listed.EnumerateArray().Select(i => i.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task AddingWithoutAStart_CountsFromTodayAtTheCurrentOdometer()
    {
        var vehicle = await AddVehicle();
        await Send("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = vehicle, date = "2026-10-01", volume = 40, totalCost = 60, odometer = 71500, isFullTank = true } });
        var input = new { vehicleId = vehicle, title = "Oil change", kind = "ODOMETER", intervalDistance = 15000 };

        var added = (await Send($"mutation($i: AddRecurringExpenseInput!) {{ addRecurringExpense(input: $i) {{ {Fields} }} }}", new { i = input })).GetProperty("data").GetProperty("addRecurringExpense");

        Assert.Equal(71500, added.GetProperty("lastDoneOdometer").GetInt64());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), added.GetProperty("lastDoneDate").GetString());
        Assert.Equal(86500, added.GetProperty("status").GetProperty("dueOdometer").GetInt64());

        var bare = await AddVehicle(); // no reading at all: the odometer has to be given
        var refused = await Send($"mutation($i: AddRecurringExpenseInput!) {{ addRecurringExpense(input: $i) {{ {Fields} }} }}", new { i = input with { vehicleId = bare } });
        Assert.Equal("recurring.odometerRequired", refused.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
    }

    [Fact]
    public async Task MarkingItDone_LogsTheExpenseAndStartsTheNextInterval()
    {
        var vehicle = await AddVehicle();
        var id = (await AddItem(vehicle)).GetProperty("data").GetProperty("addRecurringExpense").GetProperty("id").GetString();

        var done = await Send($"mutation($i: MarkRecurringExpenseDoneInput!) {{ markRecurringExpenseDone(input: $i) {{ {Fields} }} }}",
            new { i = new { id, date = "2026-09-20", odometer = 62000, createExpense = true, amount = 35000 } });

        var item = done.GetProperty("data").GetProperty("markRecurringExpenseDone");
        Assert.Equal(("2026-09-20", 62000L), (item.GetProperty("lastDoneDate").GetString(), item.GetProperty("lastDoneOdometer").GetInt64()));
        Assert.Equal("2027-09-20", item.GetProperty("status").GetProperty("dueDate").GetString());
        var expenses = (await Send("query($v: UUID!) { expenses(vehicleId: $v) { title category amount currency odometer } }", new { v = vehicle })).GetProperty("data").GetProperty("expenses");
        var expense = Assert.Single(expenses.EnumerateArray());
        Assert.Equal(("Oil change", "Service", 35000m, 62000L), (expense.GetProperty("title").GetString(), expense.GetProperty("category").GetString(), expense.GetProperty("amount").GetDecimal(), expense.GetProperty("odometer").GetInt64()));
        Assert.False(string.IsNullOrEmpty(expense.GetProperty("currency").GetString())); // the instance default
    }

    [Fact]
    public async Task Updating_AndDeleting_AndErrorsCarryTranslationKeys()
    {
        var vehicle = await AddVehicle();
        var id = (await AddItem(vehicle)).GetProperty("data").GetProperty("addRecurringExpense").GetProperty("id").GetString();

        var updated = await Send($"mutation($i: UpdateRecurringExpenseInput!) {{ updateRecurringExpense(input: $i) {{ {Fields} }} }}",
            new { i = new { id, title = "Insurance", kind = "TIME", intervalMonths = 12, lastDoneDate = "2026-03-01", warnDays = 14 } });
        var item = updated.GetProperty("data").GetProperty("updateRecurringExpense");
        Assert.Equal(("Insurance", "TIME", 14), (item.GetProperty("title").GetString(), item.GetProperty("kind").GetString(), item.GetProperty("warnDays").GetInt32()));

        var invalid = await AddItem(vehicle, new { kind = "ODOMETER", intervalDistance = (long?)null });
        var error = invalid.GetProperty("errors")[0].GetProperty("extensions");
        Assert.Equal(("VALIDATION_FAILED", "recurring.distanceInvalid"), (error.GetProperty("code").GetString(), error.GetProperty("key").GetString()));

        var gone = await Send("mutation($id: UUID!) { deleteRecurringExpense(id: $id) }", new { id });
        Assert.True(gone.GetProperty("data").GetProperty("deleteRecurringExpense").GetBoolean());
        var missing = await Send("mutation($id: UUID!) { deleteRecurringExpense(id: $id) }", new { id });
        Assert.Equal("NOT_FOUND", missing.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString());
    }
}
