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
/// </summary>
[Collection(ApiCollection.Name)]
public class LoggingTests
{
    private const string Listener = "Tankstat.Api.GraphQL.GraphQLLoggingListener";

    /// <summary>What the app itself logged (the framework's lines are not what is tested here).</summary>
    private static IEnumerable<LogEntry> Ours(TestApp app) => app.Log.Entries.Where(e => e.Category.StartsWith("Tankstat", StringComparison.Ordinal));

    private sealed class ThrowingEmail : IEmailSender
    {
        public bool IsConfigured => true;
        public Task SendAsync(string to, string subject, string body, CancellationToken ct) => throw new InvalidOperationException("smtp is down");
    }

    [Fact]
    public async Task ABusinessError_IsOnlyADebugLine_NamingTheFieldAndTheKey()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        var body = await app.NewClient().Gql("mutation { addVehicle(input: { name: \" \", fuelType: PETROL }) { id } }");

        Assert.Equal("VALIDATION_FAILED", body.ErrorCode());
        Assert.DoesNotContain(Ours(app), e => e.Level >= LogLevel.Warning);
        var line = Assert.Single(Ours(app), e => e.Category == Listener && e.Message.Contains("vehicle.nameRequired"));
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Contains("addVehicle", line.Message);
        Assert.Contains(Ours(app), e => e.Message.Contains("mutation (unnamed) finished in")); // and the request is timed, by what it ran

        await app.NewClient().Gql("query Mine { myVehicles { id } }");

        Assert.Contains(Ours(app), e => e.Message.Contains("query Mine finished in"));
    }

    [Fact]
    public async Task AnOperationNameFromTheClient_NeverReachesTheLogUnchecked()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });
        var forged = "x\nwarn: Tankstat.Application.Users.AuthService[0] User 00000000-0000-0000-0000-000000000000 signed in";

        var response = await app.NewClient().PostAsJsonAsync("/graphql", new { query = "query Mine { myVehicles { id } }", operationName = forged });

        Assert.Contains("errors", await response.Content.ReadAsStringAsync()); // answered with an error: there is no such operation
        Assert.False(app.Log.Mentions("signed in"));
        Assert.DoesNotContain(app.Log.Entries, e => e.Text.Contains('\n') && e.Category.StartsWith("Tankstat", StringComparison.Ordinal) && e.Exception is null);
    }

    [Fact]
    public async Task ARefusal_IsAWarning_NamingTheFieldAndTheKey()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" });

        var body = await app.NewClient().Gql("{ users { id } }");

        Assert.Equal("FORBIDDEN", body.ErrorCode());
        var warning = Assert.Single(Ours(app), e => e.Level == LogLevel.Warning);
        Assert.Equal(Listener, warning.Category);
        Assert.Contains("refused at users", warning.Message);
        Assert.Contains("auth.adminRequired", warning.Message);
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
        Assert.Contains("createUser", error.Message);
        Assert.Contains(Ours(app), e => e.Level == LogLevel.Information && e.Message.Contains("created user")); // what was done before it failed is on record
        Assert.False(app.Log.Mentions("new.person@example.com"));
    }

    [Fact]
    public async Task SigningIn_AndOut_IsLoggedByUserId_NeverByAddressOrPassword()
    {
        using var app = TestApp.Standalone();
        var client = app.NewClient();

        var wrong = await client.Gql("mutation($i: LoginInput!) { login(input: $i) { id } }", new { i = new { email = "root@example.com", password = "not-the-password-1" } });
        var admin = await client.LoginAs("root@example.com", "initial-password-1");
        var id = admin.GetProperty("id").GetString()!;
        await client.Gql("mutation { logout }");

        Assert.Equal("INVALID_CREDENTIALS", wrong.ErrorCode());
        Assert.Contains(Ours(app), e => e.Level == LogLevel.Warning && e.Message.Contains("wrong password") && e.Message.Contains(id));
        Assert.Contains(Ours(app), e => e.Level == LogLevel.Information && e.Message.Contains("signed in") && e.Message.Contains(id));
        Assert.Contains(Ours(app), e => e.Level == LogLevel.Information && e.Message.Contains("signed out") && e.Message.Contains(id));
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
        Assert.Contains("PUT /media/vehicles/{vehicleId:guid}/picture", warning.Message); // the route, never the path that was asked for
        Assert.DoesNotContain(car, warning.Message);
        Assert.Contains("vehicle.viewOnly", warning.Message);
        Assert.Contains(bobId, warning.Message);
        Assert.Contains($"UserId={bobId}", warning.ScopeText); // the scope of the request names the user for every line written meanwhile
    }

    [Fact]
    public async Task AStart_LogsTheMigrationsAndTheFirstAdministrator()
    {
        using var app = TestApp.Standalone();

        _ = app.NewClient(); // starts the host

        Assert.Contains(Ours(app), e => e.Level == LogLevel.Information && e.Message.StartsWith("Applying", StringComparison.Ordinal) && e.Message.Contains("database migrations"));
        Assert.Contains(Ours(app), e => e.Level == LogLevel.Information && e.Message.StartsWith("Created the first administrator", StringComparison.Ordinal));
        Assert.False(app.Log.Mentions("root@example.com"));
    }
}
