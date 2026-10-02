using System.Text.Json;

namespace Tankstat.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UserAdministrationTests : IDisposable
{
    private const string Admin = "root@example.com";
    private const string AdminPassword = "initial-password-1";

    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private async Task<HttpClient> LoggedIn(string email, string password)
    {
        var c = _app.NewClient();
        await c.LoginAs(email, password);
        return c;
    }

    /// <summary>Creates a user and completes their setup link; returns their id.</summary>
    private async Task<string> CreateUser(HttpClient admin, string email, string password)
    {
        var created = (await admin.Gql(
            "mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }",
            new { i = new { email, displayName = email, isAdmin = false } })).Data().GetProperty("createUser");
        await _app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }",
            new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = password } });
        return created.GetProperty("user").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Admin_EditsAUser_AndTheyCanSignInWithTheNewEmail()
    {
        var admin = await LoggedIn(Admin, AdminPassword);
        var id = await CreateUser(admin, "alice@example.com", "alice-password-1");

        var updated = (await admin.Gql("mutation($i: UpdateUserInput!) { updateUser(input: $i) { email displayName } }",
            new { i = new { userId = id, email = "Alice.B@example.com", displayName = "Alice B" } })).Data().GetProperty("updateUser");

        Assert.Equal("alice.b@example.com", updated.GetProperty("email").GetString());
        Assert.Equal("Alice B", updated.GetProperty("displayName").GetString());
        await LoggedIn("alice.b@example.com", "alice-password-1");
    }

    [Fact]
    public async Task SettingPasswords_IsOffUnlessConfigured()
    {
        var admin = await LoggedIn(Admin, AdminPassword);
        var id = await CreateUser(admin, "alice@example.com", "alice-password-1");
        const string Set = "mutation($id: UUID!) { setUserPassword(userId: $id, newPassword: \"brand-new-pass-1\") }";

        Assert.False((await admin.Gql("{ canSetUserPasswords }")).Data().GetProperty("canSetUserPasswords").GetBoolean());
        Assert.Equal("VALIDATION_FAILED", (await admin.Gql(Set, new { id })).ErrorCode());

        using var allowed = TestApp.Standalone(new() { ["Auth:Standalone:AllowAdminSetPassword"] = "true" });
        var allowedAdmin = allowed.NewClient();
        await allowedAdmin.LoginAs(Admin, AdminPassword);
        var created = (await allowedAdmin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } } }",
            new { i = new { email = "bob@example.com", displayName = "Bob", isAdmin = false } })).Data().GetProperty("createUser");
        var bobId = created.GetProperty("user").GetProperty("id").GetString();

        Assert.True((await allowedAdmin.Gql("{ canSetUserPasswords }")).Data().GetProperty("canSetUserPasswords").GetBoolean());
        Assert.True((await allowedAdmin.Gql(Set, new { id = bobId })).Data().GetProperty("setUserPassword").GetBoolean());
        await allowed.NewClient().LoginAs("bob@example.com", "brand-new-pass-1");
    }

    [Fact]
    public async Task DeleteUser_MovesTheirData_ToAnotherUser()
    {
        var admin = await LoggedIn(Admin, AdminPassword);
        var aliceId = await CreateUser(admin, "alice@example.com", "alice-password-1");
        var bobId = await CreateUser(admin, "bob@example.com", "bob-password-123");
        var alice = await LoggedIn("alice@example.com", "alice-password-1");
        var bob = await LoggedIn("bob@example.com", "bob-password-123");
        await alice.Gql("mutation { addVehicle(input: { name: \"Alice car\", fuelType: DIESEL }) { id } }");
        const string Delete = "mutation($i: DeleteUserInput!) { deleteUser(input: $i) }";

        Assert.Equal("VALIDATION_FAILED", (await admin.Gql(Delete, new { i = new { userId = aliceId } })).ErrorCode());
        Assert.Equal("VALIDATION_FAILED", (await admin.Gql(Delete, new { i = new { userId = aliceId, data = "MOVE" } })).ErrorCode());
        Assert.True((await admin.Gql(Delete, new { i = new { userId = aliceId, data = "MOVE", moveToUserId = bobId } })).Data().GetProperty("deleteUser").GetBoolean());

        Assert.Equal(["Alice car"], Names(await bob.Gql("{ myVehicles { name } }")));
        Assert.Equal("UNAUTHENTICATED", (await alice.Gql("{ myVehicles { id } }")).ErrorCode()); // their session is gone with them
        Assert.DoesNotContain("alice@example.com", (await admin.Gql("{ users { email } }")).Data().ToString());
    }

    [Fact]
    public async Task DeleteUser_Purge_RemovesTheirVehicles_AndAdminsCannotDeleteThemselves()
    {
        var admin = await LoggedIn(Admin, AdminPassword);
        var aliceId = await CreateUser(admin, "alice@example.com", "alice-password-1");
        var alice = await LoggedIn("alice@example.com", "alice-password-1");
        await alice.Gql("mutation { addVehicle(input: { name: \"Alice car\", fuelType: DIESEL }) { id } }");
        const string Delete = "mutation($i: DeleteUserInput!) { deleteUser(input: $i) }";
        var me = (await admin.Gql("{ session { user { id } } }")).Data().GetProperty("session").GetProperty("user").GetProperty("id").GetString();

        Assert.Equal("VALIDATION_FAILED", (await admin.Gql(Delete, new { i = new { userId = me } })).ErrorCode());
        Assert.True((await admin.Gql(Delete, new { i = new { userId = aliceId, data = "PURGE" } })).Data().GetProperty("deleteUser").GetBoolean());

        Assert.Empty(Names(await admin.Gql("{ myVehicles { name } }")));
    }

    [Fact]
    public async Task NonAdministrators_CannotManageUsers()
    {
        var admin = await LoggedIn(Admin, AdminPassword);
        var aliceId = await CreateUser(admin, "alice@example.com", "alice-password-1");
        var alice = await LoggedIn("alice@example.com", "alice-password-1");

        Assert.Equal("FORBIDDEN", (await alice.Gql("mutation($i: DeleteUserInput!) { deleteUser(input: $i) }", new { i = new { userId = aliceId } })).ErrorCode());
        Assert.Equal("FORBIDDEN", (await alice.Gql("mutation($i: UpdateUserInput!) { updateUser(input: $i) { id } }", new { i = new { userId = aliceId, email = "a@b.co" } })).ErrorCode());
        Assert.Equal("FORBIDDEN", (await alice.Gql("{ canSetUserPasswords }")).ErrorCode());
    }

    private static List<string> Names(JsonElement body) =>
        body.Data().GetProperty("myVehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToList();
}
