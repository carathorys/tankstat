using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.TestSupport;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// What an operator finds in the log of the real app: the errors nobody told the client about, refusals, sign-ins, and the start. Run
/// against the whole host (real GraphQL, real database), because the part that matters here is how HotChocolate hands its errors over.
/// Lines are found by their class and level and read through the values of their placeholders, not through their wording.
/// </summary>
[Collection(ApiCollection.Name)]
public class LoggingTests
{
    private const string Listener = "Tankstat.Api.GraphQL.GraphQLLoggingListener";
    private const string AuthService = "Tankstat.Application.Users.AuthService";

    /// <summary>What the app itself logged (the framework's lines are not what is tested here).</summary>
    private static IEnumerable<LogEntry> Ours(TestApp app) => app.Log.From("Tankstat");

    private sealed class ThrowingEmail : IEmailSender
    {
        public bool IsConfigured => true;
        public Task SendAsync(string to, string subject, string body, CancellationToken ct) => throw new InvalidOperationException("smtp is down");
    }

    [Fact]
    public async Task ABusinessError_IsOnlyADebugLine_NamingTheKey()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        var body = await app.NewClient().Gql("mutation { addVehicle(input: { name: \" \", fuelType: PETROL }) { id } }");

        Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
        Assert.DoesNotContain(Ours(app), e => e.Level >= LogLevel.Warning);
        var line = Assert.Single(app.Log.From(Listener), e => e.Values.ContainsKey("Key"));
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Equal("vehicle.nameRequired", line.Values["Key"]);
    }

    [Fact]
    public async Task EveryRequestIsTimed_ByTheOperationItRan()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });
        var client = app.NewClient();

        await client.Gql("mutation { addVehicle(input: { name: \"Car\", fuelType: PETROL }) { id } }");
        await client.Gql("query Mine { myVehicles { id } }");

        var timed = app.Log.From(Listener).Where(e => e.Values.ContainsKey("Ms")).ToList();
        Assert.All(timed, e => Assert.Equal(LogLevel.Debug, e.Level));
        Assert.Equal<object?[]>(["mutation (unnamed)", "query Mine"], timed.Select(e => e.Values["Operation"]).ToArray());
    }

    [Fact]
    public async Task ARefusal_IsAWarning_NamingTheKey()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        var body = await app.NewClient().Gql("{ users { id } }");

        Assert.Equal("FORBIDDEN", body.ErrorCode());
        var warning = Assert.Single(Ours(app), e => e.Level == LogLevel.Warning);
        Assert.Equal(Listener, warning.Category);
        Assert.Equal("auth.adminRequired", warning.Values["Key"]);
        Assert.Equal("Query.users", warning.Values["Field"]);
    }

    [Fact]
    public async Task AFieldIsNamedAsTheSchemaNamesIt_NotByTheAliasTheClientGaveIt()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });
        var alias = new string('a', 5000);

        var response = await app.NewClient().PostAsJsonAsync("/graphql", new { query = $"{{ {alias}: users {{ id }} }}" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warning = Assert.Single(Ours(app), e => e.Level == LogLevel.Warning);
        Assert.Equal("Query.users", warning.Values["Field"]);
        Assert.False(app.Log.Mentions("aaaa")); // one request must not be able to put 5,000 characters of its own text in the log
    }

    [Fact]
    public async Task AnUnexpectedException_IsLoggedOnce_WithItsException_WhileTheClientOnlySeesTheGenericMessage()
    {
        using var app = new TestApp(new()
        {
            ["Auth:Mode"] = "Standalone",
            ["Auth:Standalone:AdminEmail"] = "root@example.com",
            ["Auth:Standalone:AdminPassword"] = "initial-password-1",
            ["Auth:PublicUrl"] = "https://tank.test",
        }, s => s.AddSingleton<IEmailSender>(new ThrowingEmail())); // the last registration wins: sending the setup link fails
        var admin = app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");

        var body = await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } } }",
            new { i = new { email = "new.person@example.com", displayName = "New Person", isAdmin = false } });

        Assert.Equal("Unexpected Execution Error", body.GetProperty("errors")[0].GetProperty("message").GetString());
        var error = Assert.Single(app.Log.Entries, e => e.Level == LogLevel.Error); // once, although several hooks may hear of it
        Assert.Equal(Listener, error.Category);
        Assert.Equal("smtp is down", error.Exception!.Message);
        Assert.Equal("Mutation.createUser", error.Values["Field"]);
        // what was done before it failed is on record
        Assert.Contains(app.Log.From("Tankstat.Application.Users.UserService"), e => e.Level == LogLevel.Information && e.Values.ContainsKey("AdminId"));
        Assert.False(app.Log.Mentions("new.person@example.com"));
    }

    [Fact]
    public async Task SigningIn_AndOut_IsLoggedByUserId_NeverByAddressOrPassword()
    {
        using var app = TestApp.Standalone();
        var client = app.NewClient(); // starts the host, which says what it did at the start (another test)
        app.Log.Clear();

        var wrong = await client.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "root@example.com", password = "not-the-password-1" } });
        var admin = await client.LoginAs("root@example.com", "initial-password-1");
        var id = admin.GetProperty("id").GetString()!;
        await client.Gql("mutation { logout }");

        Assert.Equal("INVALID_CREDENTIALS", wrong.ErrorCode());
        var byId = (LogEntry e) => e.Values["UserId"]?.ToString() == id;
        Assert.Single(app.Log.From(AuthService), e => e.Level == LogLevel.Warning && byId(e)); // the wrong password
        Assert.Single(app.Log.From(AuthService), e => e.Level == LogLevel.Information && byId(e)); // the sign-in
        Assert.Single(app.Log.From("Tankstat.Api.GraphQL.AuthMutations"), e => e.Level == LogLevel.Information && byId(e)); // the sign-out
        foreach (var secret in new[] { "root@example.com", "initial-password-1", "not-the-password-1" })
            Assert.False(app.Log.Mentions(secret), secret);
    }

    [Fact]
    public async Task ARestRefusal_IsAWarning_NamingTheRouteAndTheUser_WithTheUserInTheScope()
    {
        using var app = TestApp.Standalone();
        var admin = app.NewClient();
        await admin.LoginAs("root@example.com", "initial-password-1");
        async Task<(HttpClient Client, string Id)> Create(string name)
        {
            var created = (await admin.Gql("mutation($i: CreateUserInput!) { createUser(input: $i) { user { id } reset { token } } }", new { i = new { email = $"{name}@example.com", displayName = name, isAdmin = false } })).Data().GetProperty("createUser");
            await app.NewClient().Gql("mutation($i: ResetPasswordInput!) { resetPassword(input: $i) }", new { i = new { token = created.GetProperty("reset").GetProperty("token").GetString(), newPassword = name + "-password-1" } });
            var client = app.NewClient();
            await client.LoginAs($"{name}@example.com", name + "-password-1");
            return (client, created.GetProperty("user").GetProperty("id").GetString()!);
        }
        var (alice, aliceId) = await Create("alice");
        var (bob, bobId) = await Create("bob");
        var car = (await alice.Gql("mutation { addVehicle(input: { name: \"Car\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        await admin.Gql("mutation($i: SetAccessGrantInput!) { setAccessGrant(input: $i) }", new { i = new { ownerId = aliceId, granteeId = bobId, level = "VIEW" } });
        var picture = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);
        picture.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        app.Log.Clear();

        var refused = await bob.PutAsync($"/media/vehicles/{car}/picture", picture); // a viewer may not change the vehicle
        var missing = await bob.GetAsync("/media/" + Guid.NewGuid());                // and a picture that is not there is no business of the log

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var warning = Assert.Single(Ours(app), e => e.Level >= LogLevel.Warning);
        Assert.Equal<object?[]>(["PUT", "/media/vehicles/{vehicleId:guid}/picture", "vehicle.viewOnly", bobId],
            [warning.Values["Method"], warning.Values["Route"], warning.Values["Key"], warning.Values["UserId"]?.ToString()]);
        Assert.DoesNotContain(car, warning.Message); // the route pattern, never the path that was asked for
        Assert.Contains($"UserId={bobId}", warning.ScopeText); // the scope of the request names the user for every line written meanwhile
    }

    [Fact]
    public async Task AStart_LogsTheMigrationsAndTheFirstAdministrator()
    {
        // A new, empty database (not the migrated copy the other hosts start from), so this start applies the migrations itself.
        using var app = new TestApp(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone",
            ["Auth:Standalone:AdminEmail"] = "root@example.com",
            ["Auth:Standalone:AdminPassword"] = "initial-password-1",
        }, migrated: false);

        _ = app.NewClient(); // starts the host

        Assert.Contains(app.Log.From("Tankstat.Infrastructure.Persistence.DatabaseMigrator"), e => e.Level == LogLevel.Information && e.Values.ContainsKey("Migrations"));
        Assert.Contains(app.Log.From(AuthService), e => e.Level == LogLevel.Information && e.Values.ContainsKey("UserId"));
        Assert.False(app.Log.Mentions("root@example.com"));
    }

    [Fact]
    public async Task AQueryThatDoesNotValidate_IsADebugLine_WithItsCodes_AndNotWhatTheClientWrote()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        var response = await app.NewClient().PostAsJsonAsync("/graphql", new { query = "{ nothingLikeThis }" });

        Assert.Contains("errors", await response.Content.ReadAsStringAsync()); // the client is told what is wrong with its query...
        Assert.DoesNotContain(Ours(app), e => e.Level >= LogLevel.Warning);
        var line = Assert.Single(app.Log.From(Listener), e => e.Values.ContainsKey("Codes"));
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.False(app.Log.Mentions("nothingLikeThis")); // ...the log only that it did not validate, and the error codes
    }

    [Fact]
    public async Task ARequestTheEngineTurnsDown_ForAMissingVariable_IsADebugLine_NotAnErrorWithItsText()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        // The mutation wants $i and the request sends none: the engine refuses it before anything runs, and says so in words of its own.
        var response = await app.NewClient().PostAsJsonAsync("/graphql", new { query = "mutation($i: LoginInput!) { login(input: $i) { id } }" });

        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Ours(app), e => e.Level >= LogLevel.Warning);
        Assert.False(app.Log.Mentions("LoginInput")); // the engine's text names the variable and its type: the log keeps only the codes
    }

    [Fact]
    public async Task AnOperationNameFromTheClient_NeverReachesTheLogUnchecked()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });
        var forged = "x\nwarn: forged"; // short, so only the check of its characters can turn it away

        var response = await app.NewClient().PostAsJsonAsync("/graphql", new { query = "query Mine { myVehicles { id } }", operationName = forged });

        Assert.Contains("errors", await response.Content.ReadAsStringAsync()); // answered with an error: there is no such operation
        Assert.DoesNotContain(Ours(app), e => e.Level >= LogLevel.Warning);
        Assert.False(app.Log.Mentions("forged"));
        var timed = Assert.Single(app.Log.From(Listener), e => e.Values.ContainsKey("Ms"));
        Assert.Equal("operation (invalid name)", timed.Values["Operation"]); // the name was seen, and turned away
    }
}
