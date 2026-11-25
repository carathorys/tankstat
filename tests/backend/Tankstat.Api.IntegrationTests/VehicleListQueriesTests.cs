using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// A list of vehicles costs a fixed number of database contexts, however many rows it has (#311): who is asking is checked once per
/// request, and what every row says about itself (what the user may do, its owner, its counts, its summary) is loaded for the whole page.
/// Standalone, never mode None: there the anonymous principal costs nothing, which would hide the first.
/// </summary>
public class VehicleListQueriesTests(VehicleListQueriesTests.Data data) : IClassFixture<VehicleListQueriesTests.Data>
{
    [Fact]
    public async Task WhoIsAsking_IsCheckedOncePerRequest_NotForEveryRow()
    {
        var plain = await data.CountAsync(data.Users.Alice, "{ myVehicles(skip: 0, take: 24) { id name } }");
        var withLevels = await data.CountAsync(data.Users.Alice, "{ myVehicles(skip: 0, take: 24) { id canEdit } }");
        Assert.InRange(withLevels.Contexts, 1, plain.Contexts);
        Assert.All(Rows(withLevels.Body, "myVehicles"), v => Assert.True(v.GetProperty("canEdit").GetBoolean()));

        const string grid = "vehicles(orderBy: NAME, direction: ASC, skip: 0, take: 100)";
        var adminPlain = await data.CountAsync(data.Users.Admin, $"{{ {grid} {{ id name }} }}");
        var adminWithLevels = await data.CountAsync(data.Users.Admin, $"{{ {grid} {{ id canEdit }} }}");
        Assert.Equal(100, Rows(adminWithLevels.Body, "vehicles").Count);
        Assert.InRange(adminWithLevels.Contexts, 1, adminPlain.Contexts);
    }

    [Fact]
    public async Task EveryRowsOwnerLevelsAndCounts_AreLoadedForTheWholePage()
    {
        var grid = await PageAsync(data.Users.Admin, "vehicles", take => $"{{ vehicles(orderBy: NAME, direction: ASC, skip: 0, take: {take}) {{ id canEdit logAccess owner {{ id }} refuelingCount }} }}", 100);
        Assert.Equal(100, grid.Count);
        Assert.All(grid, v =>
        {
            Assert.True(v.GetProperty("canEdit").GetBoolean());
            Assert.Equal("DELETE", v.GetProperty("logAccess").GetString());
            Assert.Equal(data.Users.AliceId, v.GetProperty("owner").GetProperty("id").GetString());
            Assert.Equal(1, v.GetProperty("refuelingCount").GetInt32());
        });

        var trash = await PageAsync(data.Users.Admin, "trash", take => $"{{ trash(take: {take}) {{ id owner {{ id }} }} }}", 100);
        Assert.Equal(20, trash.Count);
        Assert.All(trash, v => Assert.Equal(data.Users.AliceId, v.GetProperty("owner").GetProperty("id").GetString()));

        var granted = await PageAsync(data.Users.Bob, "myVehicles", take => $"{{ myVehicles(skip: 0, take: {take}) {{ id canEdit logAccess refuelingCount }} }}", 24);
        Assert.Equal(24, granted.Count);
        Assert.All(granted, v =>
        {
            Assert.False(v.GetProperty("canEdit").GetBoolean());
            Assert.Equal("VIEW", v.GetProperty("logAccess").GetString());
            Assert.Equal(1, v.GetProperty("refuelingCount").GetInt32());
        });

        var importTargets = await PageAsync(data.Users.Alice, "myVehicles", take => $"{{ myVehicles(take: {take}) {{ id canEdit logAccess }} }}", 200);
        Assert.Equal(110, importTargets.Count);
    }

    /// <summary>
    /// The home page's own query (#320): the web app's <c>Welcome</c> operation as it is sent, read from its document, never a shape of it.
    /// The access tables are read once for the request and the cards' fields take the list's vehicles: 13 contexts for the owner and a
    /// grantee alike (the principal 2, the access tables 3, the list 1, the two counts 2, the owners 1, the schedules and their readings 2,
    /// the swap reminders 1, the statistics 1), whatever the page's size. Was 27 and 35.
    /// </summary>
    [Fact]
    public async Task TheHomePagesOwnQuery_TakesThirteenContexts_ForTheOwnerAndForAGrantee()
    {
        var welcome = FrontendOperations.Document("Welcome");
        foreach (var (client, canEdit, logAccess) in new[] { (data.Users.Alice, true, "DELETE"), (data.Users.Bob, false, "VIEW") })
        {
            var few = await data.CountAsync(client, welcome, new { search = (string?)null, skip = 0, take = 3 });
            var page = await data.CountAsync(client, welcome, new { search = (string?)null, skip = 0, take = 24 });
            Assert.Equal((13, 13), (few.Contexts, page.Contexts));
            var cards = Rows(page.Body, "myVehicles");
            Assert.Equal(24, cards.Count);
            Assert.Equal(110, page.Body.Data().GetProperty("vehicleTotal").GetInt32());
            Assert.All(cards, card =>
            {
                Assert.Equal(canEdit, card.GetProperty("canEdit").GetBoolean());
                Assert.Equal(logAccess, card.GetProperty("logAccess").GetString());
                Assert.Equal(1, card.GetProperty("tyreReminders").GetArrayLength()); // #292: every car's stored set is due, read for the whole page at once
                var summary = card.GetProperty("summary");
                Assert.Equal(1, summary.GetProperty("fillUpCount").GetInt32());
                Assert.True(summary.GetProperty("latestOdometer").GetInt64() >= 1000);
            });
            Assert.Single(cards, c => c.GetProperty("recurring").GetArrayLength() == 1); // the one schedule, among the first three: both sizes pay its readings
        }
    }

    /// <summary>
    /// The fields that take the vehicle they are resolved on (#320) answer as the reads by id did: a vehicle in the trash (the trash list, a
    /// trash's own answer) has its level, no refuellings and no summary; brought back, it has them again; two aliases of one vehicle answer alike.
    /// </summary>
    [Fact]
    public async Task TheVehicleFields_OnTheirParents_AnswerAsBefore_InTheTrash_BackFromIt_AndUnderTwoAliases()
    {
        const string fields = "id logAccess refuelingCount canEdit summary { fillUpCount }";
        using var app = new TestApp(TestApp.StandaloneSettings()); // its own: the class's data stays as the other tests count it
        var users = await app.Users();
        (await users.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }",
            new { i = new { ownerId = users.AliceId, granteeId = users.BobId, level = "VIEW" } })).Data();
        var alice = users.Alice;
        var id = (await alice.Gql("mutation { addVehicle(input: { name: \"Zz trashed\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString();
        (await alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = id, date = "2026-01-10", volume = 40, totalCost = 60, currency = "EUR", odometer = 5000, isFullTank = true } })).Data();

        static string Answer(JsonElement v) =>
            $"{v.GetProperty("logAccess").GetString()} {v.GetProperty("refuelingCount").GetInt32()} {v.GetProperty("canEdit").GetBoolean()} {v.GetProperty("summary").ValueKind}";
        var trashed = (await alice.Gql($"mutation($id: UUID!) {{ deleteVehicle(id: $id) {{ {fields} }} }}", new { id })).Data().GetProperty("deleteVehicle");
        Assert.Equal("DELETE 0 True Null", Answer(trashed));
        foreach (var who in new[] { alice, users.Admin })
        {
            var row = Rows(await who.Gql($"{{ trash(take: 100) {{ {fields} }} }}"), "trash").Single(v => v.GetProperty("id").GetString() == id);
            Assert.Equal("DELETE 0 True Null", Answer(row));
        }
        var back = (await alice.Gql($"mutation($id: UUID!) {{ restoreVehicle(id: $id) {{ {fields} }} }}", new { id })).Data().GetProperty("restoreVehicle");
        Assert.Equal("DELETE 1 True Object", Answer(back));

        var aliases = (await users.Bob.Gql($"query($id: UUID!) {{ a: vehicle(id: $id) {{ {fields} }} b: vehicle(id: $id) {{ {fields} }} }}", new { id })).Data();
        Assert.Equal(("VIEW 1 False Object", "VIEW 1 False Object"), (Answer(aliases.GetProperty("a")), Answer(aliases.GetProperty("b"))));
    }

    /// <summary>
    /// A page of <paramref name="take"/> rows, which must take the very number of database contexts that a page of three takes (a fixed number,
    /// whatever the rows), and a small one.
    /// </summary>
    private async Task<List<JsonElement>> PageAsync(HttpClient client, string list, Func<int, string> query, int take)
    {
        var few = await data.CountAsync(client, query(3));
        var page = await data.CountAsync(client, query(take));
        Assert.Equal(3, Rows(few.Body, list).Count);
        Assert.Equal(few.Contexts, page.Contexts);
        Assert.InRange(page.Contexts, 1, 25);
        return Rows(page.Body, list);
    }

    private static List<JsonElement> Rows(JsonElement body, string list) => [.. body.Data().GetProperty(list).EnumerateArray()];

    /// <summary>
    /// One Standalone host for the class: Alice owns 130 vehicles, 20 of them in the trash; Bob holds a View grant on Alice's data; the
    /// administrator sees everything. Each vehicle has a refuelling, so the counts and the summaries have something to say.
    /// </summary>
    public sealed class Data : IAsyncLifetime
    {
        private readonly ContextCounter _contexts = new();
        private TestApp? _app;

        internal TwoUsers Users { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _app = new TestApp(TestApp.StandaloneSettings(), host: b => b.ConfigureTestServices(_contexts.Install));
            Users = await _app.Users();
            for (var i = 0; i < 130; i++)
            {
                var id = (await Users.Alice.Gql("mutation($n: String!) { addVehicle(input: { name: $n, fuelType: PETROL }) { id } }", new { n = $"Car {i:000}" }))
                    .Data().GetProperty("addVehicle").GetProperty("id").GetString();
                (await Users.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
                    new { i = new { vehicleId = id, date = "2026-01-10", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000 + i, isFullTank = true } })).Data();
                // A set in storage whose reminder's month is the server's (UTC) month: due on every card (#292).
                (await Users.Alice.Gql("mutation($i: AddTyreSetInput!) { addTyreSet(input: $i) { vehicleId } }",
                    new { i = new { vehicleId = id, name = "Winter", season = "WINTER", since = "2024-11-01", fitted = false, day = "2026-01-10", reminderMonth = DateTime.UtcNow.Month } })).Data();
                if (i % 13 == 0 || i % 13 == 7) (await Users.Alice.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id })).Data();
            }
            // One schedule, on the second car (the first is in the trash): among a page of three as of a full page (#320).
            var second = (await Users.Alice.Gql("{ myVehicles(skip: 0, take: 2) { id } }")).Data().GetProperty("myVehicles")[1].GetProperty("id").GetString();
            (await Users.Alice.Gql("mutation($i: AddRecurringExpenseInput!) { addRecurringExpense(input: $i) { id } }",
                new { i = new { vehicleId = second, title = "Insurance", kind = "TIME", intervalMonths = 12, lastDoneDate = "2026-01-15" } })).Data();
            (await Users.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }",
                new { i = new { ownerId = Users.AliceId, granteeId = Users.BobId, level = "VIEW" } })).Data();
        }

        /// <summary>The answer to <paramref name="query"/> and the database contexts it took.</summary>
        public async Task<(JsonElement Body, int Contexts)> CountAsync(HttpClient client, string query, object? variables = null)
        {
            var body = default(JsonElement);
            var contexts = await _contexts.CountAsync(async () => body = await client.Gql(query, variables));
            return (body, contexts);
        }

        public Task DisposeAsync()
        {
            _app?.Dispose();
            return Task.CompletedTask;
        }
    }
}
