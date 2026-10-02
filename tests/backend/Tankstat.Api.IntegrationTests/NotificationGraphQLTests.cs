using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

/// <summary>The inbox over GraphQL with real signed-in users: what access changes and due schedules produce, and who may touch it.</summary>
[Collection(ApiCollection.Name)]
public class NotificationGraphQLTests : IDisposable
{
    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private const string Fields = "id kind read count createdAt updatedAt subject { type id } context { type id } args { name value }";

    private sealed record Users(HttpClient Admin, HttpClient Alice, HttpClient Bob, string AliceId, string BobId);

    private async Task<Users> SignedInUsers()
    {
        var admin = _app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");

        async Task<(HttpClient Client, string Id)> Create(string name)
        {
            var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }", new { i = new { email = $"{name}@example.com", displayName = name, isAdmin = false } })).Data().GetProperty("createUser");
            await _app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }", new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = name + "-password-1" } });
            var client = _app.NewClient();
            await client.LoginAs($"{name}@example.com", name + "-password-1");
            return (client, created.GetProperty("user").GetProperty("id").GetString()!);
        }

        var alice = await Create("alice");
        var bob = await Create("bob");
        return new Users(admin, alice.Client, bob.Client, alice.Id, bob.Id);
    }

    private static async Task<string> AddVehicle(HttpClient owner, string name = "Shared car") =>
        (await owner.Gql("mutation($i: AddVehicleInput!) { addVehicle(input: $i) { id } }", new { i = new { name, fuelType = "PETROL" } })).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;

    private static Task<JsonElement> Share(HttpClient c, string vehicleId, string userId, string level) =>
        c.Gql("mutation($i: SetLogAccessInput!) { setVehicleLogAccess(input: $i) }", new { i = new { vehicleId, userId, level } });

    private static async Task<JsonElement[]> Inbox(HttpClient c, bool unreadOnly = false) =>
        (await c.Gql($"query($u: Boolean!) {{ notifications(unreadOnly: $u) {{ {Fields} }} }}", new { u = unreadOnly })).Data().GetProperty("notifications").EnumerateArray().ToArray();

    private static async Task<int> Unread(HttpClient c) => (await c.Gql("{ notificationCount(unreadOnly: true) }")).Data().GetProperty("notificationCount").GetInt32();

    private static string Arg(JsonElement n, string name) => n.GetProperty("args").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name).GetProperty("value").GetString()!;

    [Fact]
    public async Task SharingAVehicle_NotifiesTheGrantee_AndFurtherChangesAreFoldedIn()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);

        await Share(u.Alice, car, u.BobId, "EDIT");
        await Share(u.Alice, car, u.BobId, "DELETE");

        var n = Assert.Single(await Inbox(u.Bob));
        Assert.Equal(("LOG_ACCESS_CHANGED", false, 2), (n.GetProperty("kind").GetString(), n.GetProperty("read").GetBoolean(), n.GetProperty("count").GetInt32()));
        Assert.Equal(("VEHICLE", car), (n.GetProperty("context").GetProperty("type").GetString(), n.GetProperty("context").GetProperty("id").GetString()));
        Assert.Equal(("Shared car", "alice", "DELETE"), (Arg(n, "vehicleName"), Arg(n, "actorName"), Arg(n, "level")));
        Assert.Empty(await Inbox(u.Alice)); // she did it herself

        await Share(u.Alice, car, u.BobId, "NONE"); // undone before Bob looked
        Assert.Equal(0, await Unread(u.Bob));
    }

    [Fact]
    public async Task AnEditorSharing_AlsoNotifiesTheOwner()
    {
        var u = await SignedInUsers();
        await u.Admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = u.AliceId, granteeId = u.BobId, level = "EDIT" } });
        var car = await AddVehicle(u.Alice);
        var carol = (await u.Admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } } }", new { i = new { email = "carol@example.com", displayName = "carol", isAdmin = false } })).Data().GetProperty("createUser").GetProperty("user").GetProperty("id").GetString()!;

        Assert.Null((await Share(u.Bob, car, carol, "EDIT")).ErrorCode());

        var kinds = (await Inbox(u.Alice)).Select(n => n.GetProperty("kind").GetString()).Order();
        Assert.Equal(["DATA_SHARED", "VEHICLE_SHARED"], kinds); // the admin's grant to Bob, then Bob sharing her car
        Assert.Equal(["DATA_ACCESS_CHANGED"], (await Inbox(u.Bob)).Select(n => n.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task AnOverdueSchedule_IsNotifiedOnce_ToEveryoneWhoCanSeeIt()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        await Share(u.Alice, car, u.BobId, "EDIT");
        var schedule = (await u.Alice.Gql("mutation($i: AddRecurringExpenseInput!) { addRecurringExpense(input: $i) { id } }",
            new { i = new { vehicleId = car, title = "Insurance", kind = "TIME", intervalMonths = 12, lastDoneDate = "2020-01-01" } })).Data().GetProperty("addRecurringExpense").GetProperty("id").GetString();

        // Asking for the count and the list in one request, and again in the next, still makes one.
        await u.Alice.Gql($"{{ notificationCount notifications {{ {Fields} }} }}");
        var n = Assert.Single(await Inbox(u.Alice));
        Assert.Equal(("RECURRING_OVERDUE", "RECURRING_EXPENSE", schedule), (n.GetProperty("kind").GetString(), n.GetProperty("subject").GetProperty("type").GetString(), n.GetProperty("subject").GetProperty("id").GetString()));
        Assert.Equal(("Insurance", "Shared car"), (Arg(n, "title"), Arg(n, "vehicleName")));

        Assert.Equal(["LOG_ACCESS_CHANGED", "RECURRING_OVERDUE"], (await Inbox(u.Bob)).Select(x => x.GetProperty("kind").GetString()).Order());
        Assert.Empty(await Inbox(u.Admin)); // an administrator's right to everything does not make it their business
    }

    [Fact]
    public async Task NotificationsAreRead_AndDeleted_OnlyByTheirRecipient()
    {
        var u = await SignedInUsers();
        var car = await AddVehicle(u.Alice);
        var other = await AddVehicle(u.Alice, "Other car");
        await Share(u.Alice, car, u.BobId, "EDIT");
        await Share(u.Alice, other, u.BobId, "EDIT");
        var ids = (await Inbox(u.Bob)).Select(n => n.GetProperty("id").GetString()!).ToArray();

        Assert.Equal(0, (await u.Alice.Gql("mutation($ids: [UUID!]) { markNotificationsRead(ids: $ids) }", new { ids })).Data().GetProperty("markNotificationsRead").GetInt32());
        Assert.Equal("NOT_FOUND", (await u.Alice.Gql("mutation($id: UUID!) { deleteNotification(id: $id) }", new { id = ids[0] })).ErrorCode());
        Assert.Equal(2, await Unread(u.Bob));

        Assert.Equal(1, (await u.Bob.Gql("mutation($ids: [UUID!]) { markNotificationsRead(ids: $ids) }", new { ids = new[] { ids[0] } })).Data().GetProperty("markNotificationsRead").GetInt32());
        Assert.Equal([ids[1]], (await Inbox(u.Bob, unreadOnly: true)).Select(n => n.GetProperty("id").GetString()));
        Assert.Equal(1, (await u.Bob.Gql("mutation { markNotificationsRead }")).Data().GetProperty("markNotificationsRead").GetInt32());
        Assert.True((await u.Bob.Gql("mutation($id: UUID!) { deleteNotification(id: $id) }", new { id = ids[0] })).Data().GetProperty("deleteNotification").GetBoolean());
        Assert.Equal(1, (await u.Bob.Gql("mutation { deleteReadNotifications }")).Data().GetProperty("deleteReadNotifications").GetInt32());
        Assert.Empty(await Inbox(u.Bob));
    }

    [Fact]
    public async Task TheInbox_NeedsSomeoneSignedIn()
    {
        var anonymous = _app.NewClient();

        Assert.Equal("UNAUTHENTICATED", (await anonymous.Gql("{ notificationCount }")).ErrorCode());
    }

    [Fact]
    public async Task WithAuthenticationOff_TheAnonymousUserGetsTheReminders()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });
        var client = app.NewClient();
        var car = await AddVehicle(client);
        await client.Gql("mutation($i: AddRecurringExpenseInput!) { addRecurringExpense(input: $i) { id } }",
            new { i = new { vehicleId = car, title = "Insurance", kind = "TIME", intervalMonths = 12, lastDoneDate = "2020-01-01" } });

        Assert.Equal(1, await Unread(client));
    }
}
