using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// Adds that carry an id the client chose are idempotent end to end: the same mutation sent twice (an answer that never arrived) creates
/// one thing and answers with it both times. Every entity a client edits carries its version.
/// </summary>
[Collection(ApiCollection.Name)]
public class ClientIdGraphQLTests(ApiFixture api)
{
    private async Task<JsonElement> Data(string query, object? variables = null)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("errors", out var errors), errors.ToString());
        return body.GetProperty("data");
    }

    private const string AddVehicle = "mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id version } }";

    [Fact]
    public async Task AVehicleAndALog_SentTwiceWithTheirIds_AreCreatedOnce()
    {
        var vehicleId = Guid.NewGuid().ToString();
        var logId = Guid.NewGuid().ToString();
        var vehicle = new { id = vehicleId, name = "Client id " + vehicleId[..6], fuelType = "PETROL" };
        var log = new { id = logId, vehicleId, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true };

        for (var i = 0; i < 2; i++)
        {
            var added = (await Data(AddVehicle, new { i = vehicle })).GetProperty("addVehicle");
            Assert.Equal((vehicleId, 1), (added.GetProperty("id").GetString(), added.GetProperty("version").GetInt32()));
            var logged = (await Data("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id version } }", new { i = log })).GetProperty("logRefueling");
            Assert.Equal((logId, 1), (logged.GetProperty("id").GetString(), logged.GetProperty("version").GetInt32()));
        }

        var listed = await Data("query($v: UUID!) { refuelings(vehicleId: $v) { id version } refuelingCount(vehicleId: $v) }", new { v = vehicleId });
        Assert.Equal(1, listed.GetProperty("refuelingCount").GetInt32());
        var updated = await Data("mutation($i: UpdateRefuelingInput!) { updateRefueling(input: $i) { version } }",
            new { i = new { id = logId, date = "2026-09-01", volume = 41, totalCost = 60, odometer = 1000, isFullTank = true } });
        Assert.Equal(2, updated.GetProperty("updateRefueling").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task AnExpenseAndASchedule_SentTwiceWithTheirIds_AreCreatedOnce()
    {
        var vehicleId = (await Data(AddVehicle, new { i = new { name = "Client id expenses", fuelType = "PETROL" } })).GetProperty("addVehicle").GetProperty("id").GetString();
        var (expenseId, scheduleId) = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        for (var i = 0; i < 2; i++)
        {
            var expense = await Data("mutation($i: AddExpenseInput!) { addExpense(input: $i) { id version } }",
                new { i = new { id = expenseId, vehicleId, date = "2026-09-01", title = "Parking", amount = 5, currency = "EUR" } });
            Assert.Equal(expenseId, expense.GetProperty("addExpense").GetProperty("id").GetString());
            var schedule = await Data("mutation($i: AddRecurringExpenseInput!) { addRecurringExpense(input: $i) { id version } }",
                new { i = new { id = scheduleId, vehicleId, title = "Insurance", kind = "TIME", intervalMonths = 12, lastDoneDate = "2026-01-15" } });
            Assert.Equal((scheduleId, 1), (schedule.GetProperty("addRecurringExpense").GetProperty("id").GetString(), schedule.GetProperty("addRecurringExpense").GetProperty("version").GetInt32()));
        }

        var counts = await Data("query($v: UUID!) { expenseCount(vehicleId: $v) vehicle(id: $v) { recurring { id } } }", new { v = vehicleId });
        Assert.Equal(1, counts.GetProperty("expenseCount").GetInt32());
        Assert.Single(counts.GetProperty("vehicle").GetProperty("recurring").EnumerateArray());
    }

    [Fact]
    public async Task AVisitSentTwiceWithItsExpenseId_LogsOneExpense_AndMovesTheScheduleOnce()
    {
        var vehicleId = (await Data(AddVehicle, new { i = new { name = "Client id visit", fuelType = "PETROL" } })).GetProperty("addVehicle").GetProperty("id").GetString();
        var scheduleId = (await Data("mutation($i: AddRecurringExpenseInput!) { addRecurringExpense(input: $i) { id } }",
            new { i = new { vehicleId, title = "Oil change", kind = "TIME", intervalMonths = 12, lastDoneDate = "2026-01-15" } })).GetProperty("addRecurringExpense").GetProperty("id").GetString();
        var expenseId = Guid.NewGuid().ToString();
        const string markDone = "mutation($i: MarkRecurringExpensesDoneInput!) { markRecurringExpensesDone(input: $i) { schedules { lastDoneDate version } expense { id } } }";

        var first = (await Data(markDone, new { i = new { ids = new[] { scheduleId }, date = "2026-09-20", amount = 100, currency = "EUR", expenseId } })).GetProperty("markRecurringExpensesDone");
        var again = (await Data(markDone, new { i = new { ids = new[] { scheduleId }, date = "2026-09-25", amount = 100, currency = "EUR", expenseId } })).GetProperty("markRecurringExpensesDone");

        Assert.Equal(expenseId, first.GetProperty("expense").GetProperty("id").GetString());
        Assert.Equal(expenseId, again.GetProperty("expense").GetProperty("id").GetString());
        var schedule = Assert.Single(again.GetProperty("schedules").EnumerateArray());
        Assert.Equal(("2026-09-20", 2), (schedule.GetProperty("lastDoneDate").GetString(), schedule.GetProperty("version").GetInt32()));
        Assert.Equal(1, (await Data("query($v: UUID!) { expenseCount(vehicleId: $v) }", new { v = vehicleId })).GetProperty("expenseCount").GetInt32());
    }
}
