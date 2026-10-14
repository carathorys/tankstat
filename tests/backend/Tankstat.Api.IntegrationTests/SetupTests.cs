using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Application.Setup;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// A new instance waits for its setup: the start logs a one-time code, the notices end with <c>SETUP_PENDING</c>, and the code opens the
/// setup's steps (the first administrator in Standalone, the acknowledgement with sign-in off, a restore of the backups folder), which no
/// administrator could, since none exists. What the environment says completes it at start; a database with data counts as set up.
/// </summary>
public sealed class SetupTests : IDisposable
{
    private const string Notices = "{ notices { code severity } }";
    private const string Pending = "{ setup { pending } }";
    private const string CreateAdministrator = "mutation($i: SetupAdministratorInput!) { setupCreateAdministrator(input: $i) { email isAdmin } }";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-backups");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Standalone with no bootstrap credentials and the setup on: what <c>docker run</c> with nothing but the mode starts as.</summary>
    private static Dictionary<string, string?> WaitingStandalone(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone",
            ["Auth:Standalone:AdminEmail"] = "",
            ["Auth:Standalone:AdminPassword"] = "",
            ["Setup:Enabled"] = "true",
        };
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        return settings;
    }

    private static string CodeOf(TestApp app) =>
        (string)Assert.Single(app.Log.From<SetupService>(), e => e.Values.ContainsKey("Code")).Values["Code"]!;

    private static IEnumerable<string?> CodesOf(JsonElement body) =>
        body.Data().GetProperty("notices").EnumerateArray().Select(n => n.GetProperty("code").GetString());

    [Fact]
    public async Task StandaloneWithoutAnAdministrator_Starts_Waits_AndTheCodeMakesTheFirstAdministrator_WhoIsSignedIn()
    {
        using var app = new TestApp(WaitingStandalone());
        var browser = app.NewClient();

        Assert.True((await browser.Gql(Pending)).Data().GetProperty("setup").GetProperty("pending").GetBoolean());
        var notice = (await browser.Gql(Notices)).Data().GetProperty("notices").EnumerateArray().Last();
        Assert.Equal(("SETUP_PENDING", "INFO"), (notice.GetProperty("code").GetString(), notice.GetProperty("severity").GetString()));
        var code = CodeOf(app);
        Assert.Equal(LogLevel.Warning, Assert.Single(app.Log.From<SetupService>(), e => e.Values.ContainsKey("Code")).Level);

        Assert.Equal("setup.codeInvalid", (await browser.Gql(CreateAdministrator, new { i = new { code = "ZZZZ-ZZZZ-ZZZZ", email = "root@example.com", password = "zzzz-password-1" } })).ErrorKey());
        Assert.False(app.Log.Mentions("zzzz-password-1"));
        Assert.Contains(app.Log.From("Tankstat.Api.GraphQL.GraphQLLoggingListener"), e => e.Level == LogLevel.Warning && e.Values.TryGetValue("Key", out var k) && (string?)k == "setup.codeInvalid");

        var made = (await browser.Gql(CreateAdministrator, new { i = new { code = code.ToLowerInvariant(), email = "Root@Example.com", password = "initial-password-1" } }))
            .Data().GetProperty("setupCreateAdministrator");
        Assert.Equal(("root@example.com", true), (made.GetProperty("email").GetString(), made.GetProperty("isAdmin").GetBoolean()));

        // Signed in on this browser at once, the setup completed, the notice gone, and the code opens nothing any more.
        var session = (await browser.Gql("{ session { user { email isAdmin } } }")).Data().GetProperty("session").GetProperty("user");
        Assert.Equal("root@example.com", session.GetProperty("email").GetString());
        Assert.False((await browser.Gql(Pending)).Data().GetProperty("setup").GetProperty("pending").GetBoolean());
        Assert.DoesNotContain("SETUP_PENDING", CodesOf(await browser.Gql(Notices)));
        Assert.Equal("setup.completed", (await app.NewClient().Gql(CreateAdministrator, new { i = new { code, email = "other@example.com", password = "initial-password-1" } })).ErrorKey());
        await app.NewClient().LoginAs("root@example.com", "initial-password-1");
        Assert.False(app.Log.Mentions("initial-password-1"));
    }

    [Fact]
    public async Task WithSignInOff_TheAcknowledgement_CompletesTheSetup_AndAuthDisabledStays()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None", ["Setup:Enabled"] = "true" });
        var client = app.NewClient();
        Assert.Equal(["AUTH_DISABLED", "SETUP_PENDING"], CodesOf(await client.Gql(Notices)));
        // With sign-in off the one code is also the restore code its own line names.
        Assert.Equal(CodeOf(app), (string)Assert.Single(app.Log.From("Tankstat.Infrastructure.Backups.RestoreStep"), e => e.Values.ContainsKey("Code")).Values["Code"]!);

        Assert.Equal("setup.codeInvalid", (await client.Gql("mutation($c: String!) { setupAcknowledgeOpenAccess(code: $c) { pending } }", new { c = "nope" })).ErrorKey());
        var done = (await client.Gql("mutation($c: String!) { setupAcknowledgeOpenAccess(code: $c) { pending } }", new { c = CodeOf(app) })).Data();

        Assert.False(done.GetProperty("setupAcknowledgeOpenAccess").GetProperty("pending").GetBoolean());
        Assert.Equal(["AUTH_DISABLED"], CodesOf(await client.Gql(Notices)));
    }

    [Fact]
    public async Task WhileTheSetupWaits_TheCode_ListsTheBackupsFolder_AndAsksForARestore_WithoutAnAdministrator()
    {
        string file;
        using (var old = TestApp.Standalone(new() { ["Backups:Path"] = _folder }))
            file = Path.GetFileName((await old.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default)).Path);
        using var app = new TestApp(WaitingStandalone(new() { ["Backups:Path"] = _folder }));
        var client = app.NewClient();
        var code = CodeOf(app);

        Assert.Equal("setup.codeInvalid", (await client.Gql("query($c: String!) { setupRestorePoints(code: $c) { file } }", new { c = "nope" })).ErrorKey());
        Assert.Equal("auth.unauthenticated", (await client.Gql("{ backups { restorePoints { file } } }")).ErrorKey()); // the administrators' list stays theirs
        var point = Assert.Single((await client.Gql("query($c: String!) { setupRestorePoints(code: $c) { file sameMigration } }", new { c = code }))
            .Data().GetProperty("setupRestorePoints").EnumerateArray());
        Assert.Equal((file, true), (point.GetProperty("file").GetString(), point.GetProperty("sameMigration").GetBoolean()));

        const string Restore = "mutation($i: RestoreBackupInput!) { restoreBackup(input: $i) { file } }";
        Assert.Equal("backup.codeRequired", (await client.Gql(Restore, new { i = new { file } })).ErrorKey());
        Assert.Equal(file, (await client.Gql(Restore, new { i = new { file, code } })).Data().GetProperty("restoreBackup").GetProperty("file").GetString());

        Assert.Equal(file, RestoreMarker.Read(RestoreMarker.PathIn(_folder)).File);
        Assert.Equal(1, app.Restarts.Count);
    }

    [Fact]
    public async Task ADatabaseWithData_FromBeforeTheSetupPage_CountsAsSetUp()
    {
        using var data = new SharedData();
        using (var before = data.Host(new() { ["Auth:Mode"] = "None" }))
            (await before.NewClient().Gql("mutation { addVehicle(input: { name: \"Old car\", fuelType: PETROL }) { id } }")).Data();
        // As the release before the setup page left it: the setup columns empty.
        TestApp.ReleaseDatabase(data.DatabasePath);
        TestApp.UndecideSetup(data.DatabasePath);

        using var upgraded = data.Host(new() { ["Auth:Mode"] = "None", ["Setup:Enabled"] = "true" });

        Assert.False((await upgraded.NewClient().Gql(Pending)).Data().GetProperty("setup").GetProperty("pending").GetBoolean());
        Assert.DoesNotContain(upgraded.Log.From<SetupService>(), e => e.Values.ContainsKey("Code"));
    }

    [Fact]
    public async Task AdministratorsTheEnvironmentNames_CompleteTheSetup_WithoutThemThereIsASetupToDo()
    {
        var proxy = new Dictionary<string, string?> { ["Auth:Mode"] = "ProxyHeader", ["Auth:ProxyHeader:TrustedProxies:0"] = "10.0.0.0/8", ["Setup:Enabled"] = "true" };
        using (var waiting = new TestApp(proxy))
            Assert.True((await waiting.NewClient().Gql(Pending)).Data().GetProperty("setup").GetProperty("pending").GetBoolean());

        using var named = new TestApp(new Dictionary<string, string?>(proxy) { ["Auth:AdminEmails:0"] = "boss@example.com" });
        Assert.False((await named.NewClient().Gql(Pending)).Data().GetProperty("setup").GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task SetupTurnedOff_NeverWaits()
    {
        using var app = new TestApp(new() { ["Auth:Mode"] = "None" }); // the test hosts' default: Setup:Enabled=false

        Assert.False((await app.NewClient().Gql(Pending)).Data().GetProperty("setup").GetProperty("pending").GetBoolean());
        Assert.DoesNotContain(app.Log.From<SetupService>(), e => e.Values.ContainsKey("Code"));
    }
}
