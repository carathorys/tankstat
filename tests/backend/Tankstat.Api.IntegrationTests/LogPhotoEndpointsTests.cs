using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>Photos of expenses and refuelings over real HTTP and GraphQL: upload, list, serve, remove, access rules and cleanup.</summary>
[Collection(ApiCollection.Name)]
public class LogPhotoEndpointsTests : IDisposable
{
    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private static byte[] Png(byte marker = 0) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 1, 2, 3, 4];

    private static async Task<HttpResponseMessage> Put(HttpClient c, string path, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await c.PutAsync(path, content);
    }

    private string[] FilesOnDisk() => Directory.Exists(_app.UploadsPath) ? Directory.GetFiles(_app.UploadsPath, "*", SearchOption.AllDirectories) : [];

    private sealed record World(HttpClient Admin, HttpClient Alice, HttpClient Bob, string AliceId, string BobId, string VehicleId, string ExpenseId, string RefuelingId);

    private async Task<World> Setup()
    {
        var admin = _app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        async Task<(HttpClient, string)> Create(string name)
        {
            var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }", new { i = new { email = $"{name}@example.com", displayName = name, isAdmin = false } })).Data().GetProperty("createUser");
            await _app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }", new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = name + "-password-1" } });
            var client = _app.NewClient();
            await client.LoginAs($"{name}@example.com", name + "-password-1");
            return (client, created.GetProperty("user").GetProperty("id").GetString()!);
        }
        var (alice, aliceId) = await Create("alice");
        var (bob, bobId) = await Create("bob");
        var vehicle = (await alice.Gql("mutation { addVehicle(input: { name: \"Car\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        var expense = (await alice.Gql("mutation($i: AddExpenseInput!) { addExpense(input: $i) { id } }",
            new { i = new { vehicleId = vehicle, date = "2026-09-01", title = "Tyres", amount = 100, odometer = 1000 } })).Data().GetProperty("addExpense").GetProperty("id").GetString()!;
        var refueling = (await alice.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = vehicle, date = "2026-09-02", volume = 40, totalCost = 60, odometer = 1100, isFullTank = true } })).Data().GetProperty("logRefueling").GetProperty("id").GetString()!;
        return new World(admin, alice, bob, aliceId, bobId, vehicle, expense, refueling);
    }

    private static async Task<(string Id, string Url)> Uploaded(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("id").GetString()!, json.GetProperty("url").GetString()!);
    }

    private static async Task<List<(string Id, string Url)>> Photos(HttpClient c, string field, string id)
    {
        var query = field == "expense" ? "query($id: UUID!) { expense(id: $id) { photos { id url } } }" : "query($id: UUID!) { refueling(id: $id) { photos { id url } } }";
        var log = (await c.Gql(query, new { id })).Data().GetProperty(field);
        return log.GetProperty("photos").EnumerateArray().Select(p => (p.GetProperty("id").GetString()!, p.GetProperty("url").GetString()!)).ToList();
    }

    [Theory]
    [InlineData("expense", "expenses")]
    [InlineData("refueling", "refuelings")]
    public async Task APhotoIsUploadedListedServedAndRemoved_InTheFolderOfItsLog(string field, string segment)
    {
        var w = await Setup();
        var logId = field == "expense" ? w.ExpenseId : w.RefuelingId;

        var (id, url) = await Uploaded(await Put(w.Alice, $"/media/{segment}/{logId}/photos", Png()));

        Assert.Equal([(id, url)], await Photos(w.Alice, field, logId));
        var served = await w.Alice.GetAsync(url);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png(), await served.Content.ReadAsByteArrayAsync());
        var file = Assert.Single(FilesOnDisk());
        Assert.StartsWith(Path.Combine(_app.UploadsPath, "vehicles", Guid.Parse(w.VehicleId).ToString("N"), segment, Guid.Parse(logId).ToString("N")), file);

        Assert.Equal(HttpStatusCode.NoContent, (await w.Alice.DeleteAsync($"/media/{segment}/{logId}/photos/{id}")).StatusCode);

        Assert.Empty(await Photos(w.Alice, field, logId));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Alice.GetAsync(url)).StatusCode);
        Assert.Empty(FilesOnDisk());
    }

    [Fact]
    public async Task OnlyPicturesWithinTheLimitsAreAccepted()
    {
        var w = await Setup();
        var path = $"/media/expenses/{w.ExpenseId}/photos";

        var svg = await Put(w.Alice, path, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());
        var huge = await Put(w.Alice, path, [.. Png(), .. new byte[3 * 1024 * 1024]]);
        for (var i = 0; i < 10; i++) await Uploaded(await Put(w.Alice, path, Png((byte)i)));
        var eleventh = await Put(w.Alice, path, Png(99));

        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
        Assert.Equal("photo.tooMany", (await eleventh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString());
        Assert.Equal(10, (await Photos(w.Alice, "expense", w.ExpenseId)).Count);
    }

    [Fact]
    public async Task PhotosFollowTheAccessRulesOfTheVehiclesLogs()
    {
        var w = await Setup();
        var path = $"/media/expenses/{w.ExpenseId}/photos";
        var (id, url) = await Uploaded(await Put(w.Alice, path, Png()));

        // a stranger can neither see, add nor remove
        Assert.Equal(HttpStatusCode.NotFound, (await w.Bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(w.Bob, path, Png(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Bob.DeleteAsync($"{path}/{id}")).StatusCode);

        // someone who may only view Alice's data sees the photo, but cannot change anything
        await w.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = w.AliceId, granteeId = w.BobId, level = "VIEW" } });
        Assert.Equal(HttpStatusCode.OK, (await w.Bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(w.Bob, path, Png(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Bob.DeleteAsync($"{path}/{id}")).StatusCode);

        // a log grant on this vehicle (edit) lets them add and remove photos
        await w.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = w.AliceId, granteeId = w.BobId, level = "NONE" } });
        await w.Alice.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId = w.VehicleId, userId = w.BobId, level = "EDIT" } });
        var (bobsPhoto, _) = await Uploaded(await Put(w.Bob, path, Png(1)));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Bob.DeleteAsync($"{path}/{bobsPhoto}")).StatusCode);
    }

    [Fact]
    public async Task APhotoCannotBeRemovedThroughAnotherLog_AndAnonymousUsersAreRefused()
    {
        var w = await Setup();
        var (id, _) = await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png()));

        var wrongKind = await w.Alice.DeleteAsync($"/media/refuelings/{w.ExpenseId}/photos/{id}");
        var anonymous = await Put(_app.NewClient(), $"/media/expenses/{w.ExpenseId}/photos", Png(2));

        Assert.Equal(HttpStatusCode.NotFound, wrongKind.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Single(await Photos(w.Alice, "expense", w.ExpenseId));
    }

    [Fact]
    public async Task APhotoIsHiddenWhileItsLogIsInTheTrash_AndGoneForGoodWithIt()
    {
        var w = await Setup();
        var (_, url) = await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png()));
        await w.Alice.Gql("mutation($id: UUID!) { deleteExpense(id: $id) { id } }", new { id = w.ExpenseId });

        Assert.Equal(HttpStatusCode.NotFound, (await w.Alice.GetAsync(url)).StatusCode);
        Assert.Single(FilesOnDisk()); // still on disk: restoring brings it back
        await w.Alice.Gql("mutation($id: UUID!) { restoreExpense(id: $id) { id } }", new { id = w.ExpenseId });
        Assert.Equal(HttpStatusCode.OK, (await w.Alice.GetAsync(url)).StatusCode);

        await w.Alice.Gql("mutation($id: UUID!) { deleteExpense(id: $id) { id } }", new { id = w.ExpenseId });
        await w.Alice.Gql("mutation { emptyExpenseTrash }");

        Assert.Empty(FilesOnDisk());
    }

    [Fact]
    public async Task ListsOfLogs_ShowEachLogsOwnPhotos_AndTheTrashShowsNone()
    {
        var w = await Setup();
        var second = (await w.Alice.Gql("mutation($i: AddExpenseInput!) { addExpense(input: $i) { id } }",
            new { i = new { vehicleId = w.VehicleId, date = "2026-09-03", title = "Wash", amount = 10 } })).Data().GetProperty("addExpense").GetProperty("id").GetString()!;
        var (first1, _) = await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png(1)));
        var (first2, _) = await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png(2)));
        var (other, _) = await Uploaded(await Put(w.Alice, $"/media/expenses/{second}/photos", Png(3)));

        var list = (await w.Alice.Gql("query($v: UUID!) { expenses(vehicleId: $v) { title photos { id } } }", new { v = w.VehicleId })).Data().GetProperty("expenses");
        var byTitle = list.EnumerateArray().ToDictionary(e => e.GetProperty("title").GetString()!, e => e.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToList());

        Assert.Equal([first1, first2], byTitle["Tyres"]);
        Assert.Equal([other], byTitle["Wash"]);

        await w.Alice.Gql("mutation($id: UUID!) { deleteExpense(id: $id) { id } }", new { id = second });
        var trash = (await w.Alice.Gql("query { expenseTrash { title photos { id } } }")).Data().GetProperty("expenseTrash");
        Assert.Empty(trash.EnumerateArray().Single().GetProperty("photos").EnumerateArray()); // hidden with the log, like before
    }

    [Fact]
    public async Task PurgingAVehicle_RemovesEverythingUploadedForIt()
    {
        var w = await Setup();
        await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png(1)));
        await Uploaded(await Put(w.Alice, $"/media/refuelings/{w.RefuelingId}/photos", Png(2)));
        await Uploaded(await Put(w.Alice, $"/media/vehicles/{w.VehicleId}/picture", Png(3)));
        Assert.Equal(3, FilesOnDisk().Length);
        await w.Alice.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id = w.VehicleId });

        await w.Alice.Gql("mutation { emptyTrash }");

        Assert.Empty(FilesOnDisk());
        Assert.False(Directory.Exists(Path.Combine(_app.UploadsPath, "vehicles", Guid.Parse(w.VehicleId).ToString("N"))));
    }

    private static Task<JsonElement> DeleteUser(HttpClient admin, string userId, object input) =>
        admin.Gql("mutation($i: DeleteUserInput!) { deleteUser(input: $i) }", new { i = input });

    [Fact]
    public async Task DeletingAUserAndMovingTheirData_HandsThePhotosToTheNewOwner()
    {
        var w = await Setup();
        var (id, url) = await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png()));

        Assert.True((await DeleteUser(w.Admin, w.AliceId, new { userId = w.AliceId, data = "MOVE", moveToUserId = w.BobId })).Data().GetProperty("deleteUser").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await w.Bob.GetAsync(url)).StatusCode); // Bob owns the vehicle, so he may see its photos now
        Assert.Equal([(id, url)], await Photos(w.Bob, "expense", w.ExpenseId));
        Assert.Single(FilesOnDisk());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Bob.DeleteAsync($"/media/expenses/{w.ExpenseId}/photos/{id}")).StatusCode); // and change them
    }

    [Fact]
    public async Task DeletingAUserAndTheirData_RemovesEverythingTheyUploaded()
    {
        var w = await Setup();
        await Uploaded(await Put(w.Alice, $"/media/expenses/{w.ExpenseId}/photos", Png(1)));
        await Uploaded(await Put(w.Alice, $"/media/vehicles/{w.VehicleId}/picture", Png(2)));
        await Uploaded(await Put(w.Alice, "/media/me/avatar", Png(3)));
        Assert.Equal(3, FilesOnDisk().Length);

        await DeleteUser(w.Admin, w.AliceId, new { userId = w.AliceId, data = "PURGE" });

        Assert.Empty(FilesOnDisk());
        Assert.False(Directory.Exists(Path.Combine(_app.UploadsPath, "users", Guid.Parse(w.AliceId).ToString("N"))));
        Assert.False(Directory.Exists(Path.Combine(_app.UploadsPath, "vehicles", Guid.Parse(w.VehicleId).ToString("N"))));
    }
}
