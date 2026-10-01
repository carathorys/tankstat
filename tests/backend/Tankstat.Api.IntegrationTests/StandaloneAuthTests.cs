using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tankstat.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class StandaloneAuthTests : IDisposable
{
    private const string Admin = "root@example.com";
    private const string AdminPassword = "initial-password-1";

    private readonly TestApp _app = TestApp.Standalone();

    public void Dispose() => _app.Dispose();

    private const string CreateUser =
        "mutation($i: CreateUserInput!) { createUser(input: $i) { user { id email } reset { token url emailSent } } }";
    private const string ResetPassword = "mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }";

    /// <summary>Creates a user as the administrator and completes the setup link; returns their id.</summary>
    private async Task<string> CreateUserWithPassword(HttpClient adminClient, string email, string password)
    {
        var created = (await adminClient.Gql(CreateUser, new { i = new { email, displayName = email, isAdmin = false } }))
            .Data().GetProperty("createUser");
        var token = created.GetProperty("reset").GetProperty("token").GetString();
        var anon = _app.NewClient();
        Assert.True((await anon.Gql(ResetPassword, new { i = new { token, newPassword = password } })).Data().GetProperty("resetPassword").GetBoolean());
        return created.GetProperty("user").GetProperty("id").GetString()!;
    }

    private async Task<HttpClient> AdminClient()
    {
        var c = _app.NewClient();
        await c.LoginAs(Admin, AdminPassword);
        return c;
    }

    [Fact]
    public async Task Anonymous_SeesLoginState_ButNoData()
    {
        var c = _app.NewClient();

        var session = (await c.Gql("{ session { mode user { id } } notices { code } }")).Data();
        Assert.Equal("STANDALONE", session.GetProperty("session").GetProperty("mode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, session.GetProperty("session").GetProperty("user").ValueKind);
        Assert.Empty(session.GetProperty("notices").EnumerateArray());

        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ vehicles { id } }")).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("mutation { addVehicle(input: { name: \"x\", fuelType: PETROL }) { id } }")).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ users { id } }")).ErrorCode());
    }

    [Fact]
    public async Task Login_StartsASession_AndLogoutEndsIt()
    {
        var c = _app.NewClient();

        var user = await c.LoginAs(Admin, AdminPassword);
        Assert.True(user.GetProperty("isAdmin").GetBoolean());
        Assert.Contains("vehicles", (await c.Gql("{ vehicles { id } }")).Data().ToString());
        Assert.Equal(Admin, (await c.Gql("{ session { user { email } } }")).Data().GetProperty("session").GetProperty("user").GetProperty("email").GetString());

        Assert.True((await c.Gql("mutation { logout }")).Data().GetProperty("logout").GetBoolean());
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ vehicles { id } }")).ErrorCode());
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRejectedGenerically()
    {
        var c = _app.NewClient();

        var wrong = await c.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = Admin, password = "nope-nope-nope" } });
        var unknown = await c.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "ghost@example.com", password = "nope-nope-nope" } });

        Assert.Equal("INVALID_CREDENTIALS", wrong.ErrorCode());
        Assert.Equal(wrong.GetProperty("errors")[0].GetProperty("message").GetString(), unknown.GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal("UNAUTHENTICATED", (await c.Gql("{ vehicles { id } }")).ErrorCode());
    }

    [Fact]
    public async Task SessionCookie_IsHttpOnlyAndSameSite()
    {
        var response = await _app.NewClient().PostAsJsonAsync("/graphql", new
        {
            query = "mutation($i: LoginInput!) { login(input: $i) { id } }",
            variables = new { i = new { email = Admin, password = AdminPassword } },
        });

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("tankstat.session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountLocksAfterRepeatedFailures()
    {
        var c = _app.NewClient();
        for (var i = 0; i < 5; i++)
            await c.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = Admin, password = "wrong-wrong-wrong" } });

        var body = await c.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = Admin, password = AdminPassword } });

        Assert.Equal("INVALID_CREDENTIALS", body.ErrorCode());
    }

    [Fact]
    public async Task Users_SeeOnlyTheirOwnData_AndAdministratorsSeeEverything()
    {
        var admin = await AdminClient();
        await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        await CreateUserWithPassword(admin, "bob@example.com", "bob-password-123");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");
        var bob = _app.NewClient(); await bob.LoginAs("bob@example.com", "bob-password-123");

        await alice.Gql("mutation { addVehicle(input: { name: \"Alice car\", fuelType: DIESEL }) { id } }");
        await bob.Gql("mutation { addVehicle(input: { name: \"Bob car\", fuelType: PETROL }) { id } }");

        Assert.Equal(["Alice car"], Names(await alice.Gql("{ vehicles { name } }")));
        Assert.Equal(["Bob car"], Names(await bob.Gql("{ vehicles { name } }")));
        Assert.Equal(["Alice car", "Bob car"], Names(await admin.Gql("{ vehicles { name } }")).Order());
    }

    private static List<string> Names(System.Text.Json.JsonElement body) =>
        body.Data().GetProperty("vehicles").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToList();

    [Fact]
    public async Task InvisibleVehicle_CannotBeReadOrChangedByID()
    {
        var admin = await AdminClient();
        await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        await CreateUserWithPassword(admin, "bob@example.com", "bob-password-123");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");
        var bob = _app.NewClient(); await bob.LoginAs("bob@example.com", "bob-password-123");
        var id = (await alice.Gql("mutation { addVehicle(input: { name: \"Secret\", fuelType: LPG }) { id } }")).Data()
            .GetProperty("addVehicle").GetProperty("id").GetString();

        var read = await bob.Gql("query($id: UUID!) { vehicle(id: $id) { name refuelings { id } } }", new { id });
        var log = await bob.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
            new { i = new { vehicleId = id, date = "2026-10-01", liters = 1, totalCost = 1, odometerKm = 1, isFullTank = true } });

        Assert.Equal(System.Text.Json.JsonValueKind.Null, read.Data().GetProperty("vehicle").ValueKind);
        Assert.Equal("NOT_FOUND", log.ErrorCode());
    }

    [Fact]
    public async Task AdministratorSettings_ControlWhatOthersMayDoWithData()
    {
        var admin = await AdminClient();
        var aliceId = await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        var bobId = await CreateUserWithPassword(admin, "bob@example.com", "bob-password-123");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");
        var bob = _app.NewClient(); await bob.LoginAs("bob@example.com", "bob-password-123");
        var vehicleId = (await alice.Gql("mutation { addVehicle(input: { name: \"Shared\", fuelType: PETROL }) { id } }")).Data()
            .GetProperty("addVehicle").GetProperty("id").GetString();
        const string Log = "mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { ownerId: id } }";
        var input = new { i = new { vehicleId, date = "2026-10-01", liters = 10, totalCost = 10, odometerKm = 10, isFullTank = true } };

        // 1. per-user grant: view only
        await admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = aliceId, granteeId = bobId, level = "VIEW" } });
        Assert.Equal(["Shared"], Names(await bob.Gql("{ vehicles { name } }")));
        Assert.Equal("FORBIDDEN", (await bob.Gql(Log, input)).ErrorCode());

        // 2. upgrade the grant to edit
        await admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = aliceId, granteeId = bobId, level = "EDIT" } });
        Assert.Null((await bob.Gql(Log, input)).ErrorCode());
        var refuelings = (await alice.Gql("{ vehicles { refuelings { id } } }")).Data().GetProperty("vehicles")[0].GetProperty("refuelings");
        Assert.Equal(1, refuelings.GetArrayLength()); // belongs to the vehicle owner, so Alice sees it

        // 3. revoke, then open everything for viewing through the instance default
        await admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = aliceId, granteeId = bobId, level = "NONE" } });
        Assert.Empty(Names(await bob.Gql("{ vehicles { name } }")));
        await admin.Gql("mutation { setDefaultAccess(level: VIEW) { defaultLevelForOthers } }");
        Assert.Equal(["Shared"], Names(await bob.Gql("{ vehicles { name } }")));
        Assert.Equal("FORBIDDEN", (await bob.Gql(Log, input)).ErrorCode());
    }

    [Fact]
    public async Task ViewGrant_CanSeeButNotEditOrDelete_EditGrantCan_AndTrashFollowsEditRights()
    {
        var admin = await AdminClient();
        var aliceId = await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        var bobId = await CreateUserWithPassword(admin, "bob@example.com", "bob-password-123");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");
        var bob = _app.NewClient(); await bob.LoginAs("bob@example.com", "bob-password-123");
        var id = (await alice.Gql("mutation { addVehicle(input: { name: \"Shared car\", fuelType: PETROL }) { id } }")).Data()
            .GetProperty("addVehicle").GetProperty("id").GetString();
        const string Grant = "mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }";
        const string Edit = "mutation($i: UpdateVehicleInput!) { updateVehicle(input: $i) { name } }";
        const string Delete = "mutation($id: UUID!) { deleteVehicle(id: $id) { id } }";
        var edit = new { i = new { id, name = "Bob edit", fuelType = "DIESEL" } };

        await admin.Gql(Grant, new { i = new { ownerId = aliceId, granteeId = bobId, level = "VIEW" } });
        var seen = (await bob.Gql("{ vehicles { canEdit ownerName } }")).Data().GetProperty("vehicles")[0];
        Assert.False(seen.GetProperty("canEdit").GetBoolean());
        Assert.Equal("alice@example.com", seen.GetProperty("ownerName").GetString());
        Assert.Equal("FORBIDDEN", (await bob.Gql(Edit, edit)).ErrorCode());
        Assert.Equal("FORBIDDEN", (await bob.Gql(Delete, new { id })).ErrorCode());

        await admin.Gql(Grant, new { i = new { ownerId = aliceId, granteeId = bobId, level = "EDIT" } });
        Assert.True((await bob.Gql("{ vehicles { canEdit } }")).Data().GetProperty("vehicles")[0].GetProperty("canEdit").GetBoolean());
        Assert.Equal("Bob edit", (await bob.Gql(Edit, edit)).Data().GetProperty("updateVehicle").GetProperty("name").GetString());
        await bob.Gql(Delete, new { id });

        // Alice (owner) and Bob (editor) both see it in the trash; the instance default of "view" would not be enough.
        Assert.Equal(1, (await alice.Gql("{ trash { id } }")).Data().GetProperty("trash").GetArrayLength());
        Assert.Equal(1, (await bob.Gql("{ trash { id } }")).Data().GetProperty("trash").GetArrayLength());
        await admin.Gql(Grant, new { i = new { ownerId = aliceId, granteeId = bobId, level = "VIEW" } });
        Assert.Equal(0, (await bob.Gql("{ trash { id } }")).Data().GetProperty("trash").GetArrayLength());
        Assert.Equal(0, (await bob.Gql("mutation { emptyTrash }")).Data().GetProperty("emptyTrash").GetInt32());
        Assert.Equal(1, (await alice.Gql("mutation { emptyTrash }")).Data().GetProperty("emptyTrash").GetInt32());
    }

    [Fact]
    public async Task Trash_IsPrivateToOtherUsers_AndAnonymousCannotUseIt()
    {
        var admin = await AdminClient();
        await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        await CreateUserWithPassword(admin, "bob@example.com", "bob-password-123");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");
        var bob = _app.NewClient(); await bob.LoginAs("bob@example.com", "bob-password-123");
        var id = (await alice.Gql("mutation { addVehicle(input: { name: \"Private\", fuelType: PETROL }) { id } }")).Data()
            .GetProperty("addVehicle").GetProperty("id").GetString();
        await alice.Gql("mutation($id: UUID!) { deleteVehicle(id: $id) { id } }", new { id });

        Assert.Equal(0, (await bob.Gql("{ trash { id } }")).Data().GetProperty("trash").GetArrayLength());
        Assert.Equal("NOT_FOUND", (await bob.Gql("mutation($id: UUID!) { restoreVehicle(id: $id) { id } }", new { id })).ErrorCode());
        Assert.Equal(0, (await bob.Gql("mutation { emptyTrash }")).Data().GetProperty("emptyTrash").GetInt32());
        Assert.Equal(1, (await admin.Gql("{ trash { id } }")).Data().GetProperty("trash").GetArrayLength());
        Assert.Equal("UNAUTHENTICATED", (await _app.NewClient().Gql("{ trash { id } }")).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await _app.NewClient().Gql("mutation { emptyTrash }")).ErrorCode());
    }

    [Fact]
    public async Task OrdinaryUsers_CannotUseAdministratorFeatures()
    {
        var admin = await AdminClient();
        await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");

        Assert.Equal("FORBIDDEN", (await alice.Gql("{ users { id } }")).ErrorCode());
        Assert.Equal("FORBIDDEN", (await alice.Gql("{ accessSettings { defaultLevelForOthers } }")).ErrorCode());
        Assert.Equal("FORBIDDEN", (await alice.Gql("mutation { setDefaultAccess(level: EDIT) { defaultLevelForOthers } }")).ErrorCode());
        Assert.Equal("FORBIDDEN", (await alice.Gql(CreateUser, new { i = new { email = "x@example.com", isAdmin = true } })).ErrorCode());
    }

    [Fact]
    public async Task ChangingThePassword_SignsOutOtherSessions_ButNotTheCurrentOne()
    {
        var admin = await AdminClient();
        await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        var first = _app.NewClient(); await first.LoginAs("alice@example.com", "alice-password-1");
        var second = _app.NewClient(); await second.LoginAs("alice@example.com", "alice-password-1");

        var changed = await first.Gql("mutation($i: ChangePasswordInput!) { changePassword(input: $i) }",
            new { i = new { currentPassword = "alice-password-1", newPassword = "alice-new-password" } });

        Assert.True(changed.Data().GetProperty("changePassword").GetBoolean());
        Assert.Null((await first.Gql("{ vehicles { id } }")).ErrorCode());
        Assert.Equal("UNAUTHENTICATED", (await second.Gql("{ vehicles { id } }")).ErrorCode());
        await _app.NewClient().LoginAs("alice@example.com", "alice-new-password");
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Fails()
    {
        var admin = await AdminClient();

        var body = await admin.Gql("mutation($i: ChangePasswordInput!) { changePassword(input: $i) }",
            new { i = new { currentPassword = "wrong-password-1", newPassword = "alice-new-password" } });

        Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
    }

    [Fact]
    public async Task DisablingAUser_EndsTheirSessionImmediately()
    {
        var admin = await AdminClient();
        var aliceId = await CreateUserWithPassword(admin, "alice@example.com", "alice-password-1");
        var alice = _app.NewClient(); await alice.LoginAs("alice@example.com", "alice-password-1");
        Assert.Null((await alice.Gql("{ vehicles { id } }")).ErrorCode());

        await admin.Gql("mutation($id: UUID!) { setUserDisabled(userId: $id, disabled: true) { isDisabled } }", new { id = aliceId });

        Assert.Equal("UNAUTHENTICATED", (await alice.Gql("{ vehicles { id } }")).ErrorCode());
        Assert.Equal("INVALID_CREDENTIALS", (await _app.NewClient().Gql("mutation($i: LoginInput!) { login(input: $i) { id } }",
            new { i = new { email = "alice@example.com", password = "alice-password-1" } })).ErrorCode());
    }

    [Fact]
    public async Task DemotedAdministrator_LosesRightsOnTheNextRequest()
    {
        var admin = await AdminClient();
        var id = (await admin.Gql(CreateUser, new { i = new { email = "second@example.com", isAdmin = true } })).Data()
            .GetProperty("createUser").GetProperty("user").GetProperty("id").GetString();
        var token = (await admin.Gql("mutation($id: UUID!) { issuePasswordReset(userId: $id) { token } }", new { id })).Data()
            .GetProperty("issuePasswordReset").GetProperty("token").GetString();
        await _app.NewClient().Gql(ResetPassword, new { i = new { token, newPassword = "second-password-1" } });
        var second = _app.NewClient(); await second.LoginAs("second@example.com", "second-password-1");
        Assert.Null((await second.Gql("{ users { id } }")).ErrorCode());

        await admin.Gql("mutation($id: UUID!) { setUserAdmin(userId: $id, isAdmin: false) { isAdmin } }", new { id });

        Assert.Equal("FORBIDDEN", (await second.Gql("{ users { id } }")).ErrorCode());
    }

    [Fact]
    public async Task PasswordReset_RequestIsSilent_AndNeverRevealsAccounts()
    {
        var c = _app.NewClient();

        var known = await c.Gql("mutation { requestPasswordReset(email: \"root@example.com\") }");
        var unknown = await c.Gql("mutation { requestPasswordReset(email: \"ghost@example.com\") }");

        Assert.True(known.Data().GetProperty("requestPasswordReset").GetBoolean());
        Assert.True(unknown.Data().GetProperty("requestPasswordReset").GetBoolean());
    }

    [Fact]
    public async Task ResetLink_WorksOnce()
    {
        var admin = await AdminClient();
        var created = (await admin.Gql(CreateUser, new { i = new { email = "alice@example.com", isAdmin = false } })).Data().GetProperty("createUser");
        var reset = created.GetProperty("reset");
        var token = reset.GetProperty("token").GetString();
        Assert.False(reset.GetProperty("emailSent").GetBoolean()); // no SMTP configured here

        var first = await _app.NewClient().Gql(ResetPassword, new { i = new { token, newPassword = "first-password-12" } });
        var second = await _app.NewClient().Gql(ResetPassword, new { i = new { token, newPassword = "second-password-12" } });

        Assert.True(first.Data().GetProperty("resetPassword").GetBoolean());
        Assert.Equal("VALIDATION_FAILED", second.ErrorCode());
        await _app.NewClient().LoginAs("alice@example.com", "first-password-12");
    }

    [Fact]
    public async Task Introspection_NeverExposesPasswordData()
    {
        var c = _app.NewClient();

        var body = (await c.Gql("{ __schema { types { name fields { name } } } }")).ToString().ToLowerInvariant();

        Assert.DoesNotContain("passwordhash", body);
        Assert.DoesNotContain("secrethash", body);
    }

    [Fact]
    public async Task Health_StaysAvailableWithoutSignIn()
    {
        var c = _app.NewClient();

        Assert.Equal("ok", (await c.Gql("{ health { status } }")).Data().GetProperty("health").GetProperty("status").GetString());
    }
}

[Collection(ApiCollection.Name)]
public class StandaloneStartupTests
{
    [Fact]
    public void Startup_Fails_WithoutAnAdministratorOrBootstrapConfiguration()
    {
        // Blank administrator settings: whatever appsettings.json provides must not matter.
        using var app = new TestApp(new() { ["Auth:Mode"] = "Standalone", ["Auth:Standalone:AdminEmail"] = "", ["Auth:Standalone:AdminPassword"] = "" });

        var e = Assert.ThrowsAny<Exception>(() => app.NewClient());

        Assert.Contains("Auth:Standalone:AdminEmail", e.ToString());
    }

    [Fact]
    public void Startup_Fails_ForIncompleteOidcConfiguration()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "Oidc" });

        var e = Assert.ThrowsAny<Exception>(() => app.NewClient());

        Assert.Contains("Auth:Oidc:Authority", e.ToString());
    }

    [Fact]
    public void Startup_Fails_ForProxyModeWithoutTrustedProxies()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "ProxyHeader" });

        var e = Assert.ThrowsAny<Exception>(() => app.NewClient());

        Assert.Contains("TrustedProxies", e.ToString());
    }
}
