using System.Net.Http.Json;
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

    [Fact]
    public async Task WhatWasChangedLast_SaysWhoDidItWhatAndWhen()
    {
        var people = await _app.Users();
        var car = (await people.Alice.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var log = (await people.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data().GetProperty("logRefueling").GetProperty("id").GetString()!;
        await people.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = car, userId = people.BobId, level = "EDIT" } });
        const string Last = "query($id: UUID!) { refueling(id: $id) { lastChange changedAt changedBy { displayName } } }";
        var logged = (await people.Alice.Gql(Last, new { id = log })).Data().GetProperty("refueling");
        Assert.Equal(("CREATED", "alice"), (logged.GetProperty("lastChange").GetString(), logged.GetProperty("changedBy").GetProperty("displayName").GetString()));
        Assert.NotEqual(JsonValueKind.Null, logged.GetProperty("changedAt").ValueKind);

        await people.Bob.Gql("mutation($i: UpdateRefuelingInput!) { updateRefueling(input: $i) { id } }",
            new { i = new { id = log, date = "2026-09-01", volume = 41, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } });
        var edited = (await people.Alice.Gql(Last, new { id = log })).Data().GetProperty("refueling");
        Assert.Equal(("EDITED", "bob"), (edited.GetProperty("lastChange").GetString(), edited.GetProperty("changedBy").GetProperty("displayName").GetString()));

        await Send(people.Alice, new { id = Guid.NewGuid(), deleteVehicle = car }); // through a sync too
        var trashed = (await people.Alice.Gql("{ trash { lastChange changedBy { displayName } } }")).Data().GetProperty("trash").EnumerateArray().Single();
        Assert.Equal(("TRASHED", "alice"), (trashed.GetProperty("lastChange").GetString(), trashed.GetProperty("changedBy").GetProperty("displayName").GetString()));
    }

    private const string Parked = "{ parkedChanges { id kind status change reason { key } submittedBy { displayName } canResolve vehicleId } }";
    private const string Resolve = "mutation($i: ResolveSyncChangeInput!) { resolveSyncChange(input: $i) { id status entityId version } }";

    /// <summary>Bob (an editor of Alice's car) renames it from an old version on a device: parked, filed with the car.</summary>
    private static async Task<(TwoUsers People, string Car, string ChangeId)> ParkedByBob(TestApp app)
    {
        var people = await app.Users();
        var car = (await people.Alice.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var log = (await people.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data().GetProperty("logRefueling").GetProperty("id").GetString()!;
        await people.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = car, userId = people.BobId, level = "EDIT" } });
        await people.Alice.Gql("mutation($i: UpdateRefuelingInput!) { updateRefueling(input: $i) { id } }",
            new { i = new { id = log, date = "2026-09-01", volume = 41, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } }); // version 2
        var changeId = Guid.NewGuid().ToString();
        await Send(people.Bob, new { id = changeId, expectedVersion = 1, updateRefueling = new { id = log, date = "2026-09-01", volume = 45, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } });
        return (people, car, changeId);
    }

    [Fact]
    public async Task AParkedChange_IsListedForTheOwnerAndTheSender_CountedOnTheVehicle_AndNotified()
    {
        var (people, car, changeId) = await ParkedByBob(_app);

        var alices = (await people.Alice.Gql(Parked)).Data().GetProperty("parkedChanges");
        var only = Assert.Single(alices.EnumerateArray());
        Assert.Equal((changeId, "UPDATE_REFUELING", "sync.versionMismatch", "bob", true, car),
            (only.GetProperty("id").GetString(), only.GetProperty("kind").GetString(), only.GetProperty("reason").GetProperty("key").GetString(),
             only.GetProperty("submittedBy").GetProperty("displayName").GetString(), only.GetProperty("canResolve").GetBoolean(), only.GetProperty("vehicleId").GetString()));
        Assert.Contains("\"volume\":45", only.GetProperty("change").GetString());
        Assert.Single((await people.Bob.Gql(Parked)).Data().GetProperty("parkedChanges").EnumerateArray());
        Assert.Equal(1, (await people.Alice.Gql($"{{ vehicle(id: \"{car}\") {{ parkedChangeCount }} }}")).Data().GetProperty("vehicle").GetProperty("parkedChangeCount").GetInt32());

        var inbox = (await people.Alice.Gql("{ notifications(unreadOnly: true) { kind subject { type id } args { name value } } }")).Data().GetProperty("notifications");
        var note = inbox.EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "SYNC_CHANGE_PARKED");
        Assert.Equal(("SYNC_CHANGE", changeId), (note.GetProperty("subject").GetProperty("type").GetString(), note.GetProperty("subject").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AParkedChange_IsAppliedAnywayAsEdited_OrDiscarded_OnceOnly()
    {
        var (people, car, changeId) = await ParkedByBob(_app);
        var log = (await people.Alice.Gql($"{{ refuelings(vehicleId: \"{car}\", orderBy: DATE, direction: DESC, skip: 0, take: 5) {{ id }} }}"))
            .Data().GetProperty("refuelings")[0].GetProperty("id").GetString()!;

        var applied = (await people.Alice.Gql(Resolve, new { i = new { id = changeId, action = "APPLY", change = new { id = changeId,
            updateRefueling = new { id = log, date = "2026-09-01", volume = 44, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } } } })).Data().GetProperty("resolveSyncChange");
        var twice = await people.Alice.Gql(Resolve, new { i = new { id = changeId, action = "DISCARD" } });

        Assert.Equal(("APPLIED", 3), (applied.GetProperty("status").GetString(), applied.GetProperty("version").GetInt32()));
        Assert.Equal("sync.notParked", twice.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Empty((await people.Alice.Gql(Parked)).Data().GetProperty("parkedChanges").EnumerateArray());
        Assert.Equal(44, (await people.Alice.Gql($"{{ refueling(id: \"{log}\") {{ volume }} }}")).Data().GetProperty("refueling").GetProperty("volume").GetDecimal());
    }

    [Fact]
    public async Task OnlyThoseWhoMaySeeIt_FindAParkedChange_AndAnEditMustDoTheSame()
    {
        var (people, _, changeId) = await ParkedByBob(_app);
        var stranger = _app.NewClient();
        await people.Admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } } }", new { i = new { email = "eve@example.com", displayName = "eve", isAdmin = false } });

        var otherKind = await people.Alice.Gql(Resolve, new { i = new { id = changeId, action = "APPLY", change = new { id = changeId, deleteRefueling = Guid.NewGuid() } } });
        var discarded = (await people.Bob.Gql(Resolve, new { i = new { id = changeId, action = "DISCARD" } })).Data().GetProperty("resolveSyncChange");

        Assert.Equal("sync.kindMismatch", otherKind.GetProperty("errors")[0].GetProperty("extensions").GetProperty("key").GetString());
        Assert.Equal("DISCARDED", discarded.GetProperty("status").GetString());
        Assert.Equal("UNAUTHENTICATED", (await stranger.Gql(Parked)).ErrorCode());
    }

    private static async Task<string> UploadDraft(HttpClient c, string vehicleId)
    {
        var content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        var response = await c.PutAsync($"/media/vehicles/{vehicleId}/photo-drafts", content);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<string[]> Photos(HttpClient c, string refuelingId) =>
        [.. (await c.Gql($"{{ refueling(id: \"{refuelingId}\") {{ photos {{ id }} }} }}")).Data().GetProperty("refueling").GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("id").GetString()!)];

    [Fact]
    public async Task APhotoAddedOrRemovedOffline_OnASavedLog_IsAppliedOnce_AndARemovedOneIsParked()
    {
        var people = await _app.Users();
        var car = (await people.Alice.Gql("mutation { addVehicle(input: { name: \"Golf\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var log = (await people.Alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = car, date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true } })).Data().GetProperty("logRefueling").GetProperty("id").GetString()!;
        var draft = await UploadDraft(people.Alice, car);
        var add = new { id = Guid.NewGuid(), addRefuelingPhoto = new { logId = log, draftId = draft } };

        var added = await Send(people.Alice, add);
        var again = await Send(people.Alice, add);
        var removed = await Send(people.Alice, new { id = Guid.NewGuid(), removeRefuelingPhoto = new { logId = log, imageId = draft } });
        var gone = await Send(people.Alice, new { id = Guid.NewGuid(), removeRefuelingPhoto = new { logId = log, imageId = draft } });

        Assert.Equal(("APPLIED", draft), (added.GetProperty("results")[0].GetProperty("status").GetString(), added.GetProperty("results")[0].GetProperty("entityId").GetString()));
        Assert.Equal(added.GetRawText(), again.GetRawText());
        Assert.Equal("APPLIED", removed.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.Equal(("PARKED", "photo.notFound"), (gone.GetProperty("results")[0].GetProperty("status").GetString(), gone.GetProperty("results")[0].GetProperty("reason").GetProperty("key").GetString()));
        Assert.Empty(await Photos(people.Alice, log));
        var parked = Assert.Single((await people.Alice.Gql("{ parkedChanges { kind vehicleId } }")).Data().GetProperty("parkedChanges").EnumerateArray());
        Assert.Equal(("REMOVE_REFUELING_PHOTO", car), (parked.GetProperty("kind").GetString(), parked.GetProperty("vehicleId").GetString()));
    }

    [Fact]
    public async Task EveryKindOfChange_IsAppliedThroughSync_InTheOrderItWasMade()
    {
        var alice = (await _app.Users()).Alice;
        var (car, refueling, expense, schedule, visit) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var fill = new { date = "2026-09-01", volume = 40, totalCost = 60, currency = "EUR", odometer = 1000, isFullTank = true };
        var cost = new { date = "2026-09-05", title = "Parking", amount = 5, currency = "EUR" };
        var cycle = new { title = "Service", kind = "TIME", intervalMonths = 12, lastDoneDate = "2026-01-01" };

        // Each change as the device sends it: its own id and one operation (an add's id is the id of what it adds).
        var vehicle = await Send(alice, new { id = car, addVehicle = new { id = car, name = "Golf", fuelType = "PETROL" } });
        var (refuelingPhoto, expensePhoto) = (await UploadDraft(alice, car.ToString()), await UploadDraft(alice, car.ToString()));
        var rest = await Send(alice,
            new { id = refueling, logRefueling = new { id = refueling, vehicleId = car, fill.date, fill.volume, fill.totalCost, fill.currency, fill.odometer, fill.isFullTank } },
            new { id = expense, addExpense = new { id = expense, vehicleId = car, cost.date, cost.title, cost.amount, cost.currency } },
            new { id = schedule, addRecurringExpense = new { id = schedule, vehicleId = car, cycle.title, cycle.kind, cycle.intervalMonths, cycle.lastDoneDate } },
            new { id = Guid.NewGuid(), updateRefueling = new { id = refueling, fill.date, volume = 41, fill.totalCost, fill.currency, fill.odometer, fill.isFullTank } },
            new { id = Guid.NewGuid(), updateExpense = new { id = expense, cost.date, title = "Parking at the station", cost.amount, cost.currency } },
            new { id = Guid.NewGuid(), updateRecurringExpense = new { id = schedule, title = "Yearly service", cycle.kind, cycle.intervalMonths, cycle.lastDoneDate } },
            new { id = Guid.NewGuid(), updateVehicle = new { id = car, name = "Golf GTI", fuelType = "PETROL" } },
            new { id = visit, markRecurringExpensesDone = new { ids = new[] { schedule }, date = "2026-09-10", odometer = 1100, amount = 120, currency = "EUR", expenseId = visit } },
            new { id = Guid.NewGuid(), addRefuelingPhoto = new { logId = refueling, draftId = refuelingPhoto } },
            new { id = Guid.NewGuid(), removeRefuelingPhoto = new { logId = refueling, imageId = refuelingPhoto } },
            new { id = Guid.NewGuid(), addExpensePhoto = new { logId = expense, draftId = expensePhoto } },
            new { id = Guid.NewGuid(), removeExpensePhoto = new { logId = expense, imageId = expensePhoto } },
            new { id = Guid.NewGuid(), deleteRefueling = refueling },
            new { id = Guid.NewGuid(), restoreRefueling = refueling },
            new { id = Guid.NewGuid(), deleteExpense = expense },
            new { id = Guid.NewGuid(), restoreExpense = expense },
            new { id = Guid.NewGuid(), deleteRecurringExpense = schedule },
            new { id = Guid.NewGuid(), deleteVehicle = car },
            new { id = Guid.NewGuid(), restoreVehicle = car });

        var results = vehicle.GetProperty("results").EnumerateArray().Concat(rest.GetProperty("results").EnumerateArray()).ToList();
        Assert.Equal(20, results.Count);
        Assert.All(results, r => Assert.Equal("APPLIED", r.GetProperty("status").GetString()));
        var now = (await alice.Gql($"{{ vehicle(id: \"{car}\") {{ name }} refueling(id: \"{refueling}\") {{ volume deletedAt photos {{ id }} }} expense(id: \"{visit}\") {{ amount }} }}")).Data();
        Assert.Equal(("Golf GTI", 41m, true, 0, 120m),
            (now.GetProperty("vehicle").GetProperty("name").GetString(), now.GetProperty("refueling").GetProperty("volume").GetDecimal(),
             now.GetProperty("refueling").GetProperty("deletedAt").ValueKind == JsonValueKind.Null, now.GetProperty("refueling").GetProperty("photos").GetArrayLength(),
             now.GetProperty("expense").GetProperty("amount").GetDecimal()));
    }
}
