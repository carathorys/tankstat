using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Backups;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The settings file goes into every backup and comes back with a restore (#140): written back over this server's file (kept as
/// <c>.bak</c>), keeping this machine's own keys and the passwords a plain backup cannot bring, and judged before anything changes as the
/// server would run with it, here and now: its sign-in decides whether the backup needs an administrator. The in-memory settings of a test
/// host rank as the environment, so a host whose sign-in should come from its file leaves <c>Auth:Mode</c> out of them; then nothing in the
/// process's environment may set it (<see cref="ApiFixture"/> sets <c>Auth__Mode</c> while its tests run), so these run on their own.
/// </summary>
[Collection(AloneCollection.Name)]
public sealed class RestoreSettingsTests : IDisposable
{
    private const string Restore = "mutation($i: RestoreBackupInput!) { restoreBackup(input: $i) { file } }";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-restore-settings");
    private string Folder(string name) => Path.Combine(_root, name);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A host with folders of its own, its settings file written before it is built, its sign-in from <paramref name="mode"/> (none: from the file).</summary>
    private TestApp Host(string name, string? mode, string? file, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Backups:Path"] = Folder(name + "-backups"),
            ["Storage:Path"] = Folder(name + "-uploads"),
            ["Storage:PhotosPath"] = Folder(name + "-photos"),
            ["DataProtection:KeysPath"] = Folder(name + "-keys"),
        };
        if (mode is not null) settings["Auth:Mode"] = mode;
        else if (new ConfigurationBuilder().AddEnvironmentVariables().Build()["Auth:Mode"] is not null)
            throw new InvalidOperationException("Auth__Mode is set in the test process's environment: it would win over the host's settings file, which this test is about.");
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        var app = new TestApp(settings);
        if (file is not null) File.WriteAllText(app.SettingsPath, file); // read as the host is built
        return app;
    }

    private static Task<BackupResult> Backup(TestApp app) => app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default);

    /// <summary>A backup put into another host's folder, with the marker that asks for it at its start (as the mutation or the command writes it).</summary>
    private static void Stage(string folder, string backup, string? identity = null, bool marker = true)
    {
        Directory.CreateDirectory(folder);
        File.Copy(backup, Path.Combine(folder, Path.GetFileName(backup)));
        if (marker) new RestoreMarker(Path.GetFileName(backup), Guid.Empty, DateTimeOffset.UtcNow, identity).Write(folder);
    }

    private static JsonNode Json(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    [Fact]
    public async Task ARestore_WritesTheBackupsSettingsFile_KeepsTheOldOneAsBak_AndThisMachinesOwnKeysAndPasswords_ThenRestartsAtOnce()
    {
        string backup;
        using (var a = Host("a", "None", """{ "Defaults": { "Currency": "XYZ" }, "Smtp": { "Password": "zzzz-a-password" }, "Storage": { "Path": "/a/own" } }"""))
        {
            var written = await Backup(a);
            Assert.Equal(BackupSettingsState.Included, written.Manifest.Settings!.State);
            Assert.Equal(["Smtp:Password", "Storage:Path"], written.Manifest.Settings.Omitted); // a plain backup: no secret, and never the machine's own
            backup = written.Path;
        }
        var bFile = """{ "Defaults": { "Currency": "EUR" }, "Smtp": { "Password": "b-password" }, "Storage": { "Path": "/b/own" } }""";
        using var b = Host("b", "None", bFile);
        Stage(Folder("b-backups"), backup);

        _ = b.Factory.Services; // the start restores

        var restored = Json(b.SettingsPath);
        Assert.Equal("XYZ", (string?)restored["Defaults"]!["Currency"]);
        Assert.Equal("b-password", (string?)restored["Smtp"]!["Password"]); // this server's: the plain backup had none to bring
        Assert.Equal("/b/own", (string?)restored["Storage"]!["Path"]); // this machine's folder
        Assert.Equal(bFile, File.ReadAllText(b.SettingsPath + ".bak"));
        Assert.Equal(1, b.Restarts.NowCount); // the next start reads it; nothing after the restore ran under the old file in a real process
        Assert.False(File.Exists(RestoreMarker.PathIn(Folder("b-backups"))));
        Assert.False(b.Log.Mentions("zzzz-a-password"));
    }

    [Fact]
    public async Task AFileTheStartLeftOut_IsLeftOutOfTheBackup_UntilASaveTakesAFileIn()
    {
        using var a = Host("a", "None", """{ "Notifications": { "MaxPerHour": "0" } }""");
        Assert.True(a.Factory.Services.GetRequiredService<SettingsFileStatus>().Ignored);
        Assert.Equal(BackupSettingsState.Ignored, (await Backup(a)).Manifest.Settings!.State);

        // What a save on the settings page does over a file the start left out: the file written, then taken into the running configuration.
        await a.Factory.Services.GetRequiredService<ISettingsFileStore>().WriteAsync(new Dictionary<string, string?> { ["Defaults:Currency"] = "XYZ" }, default);
        a.Factory.Services.GetRequiredService<ISystemConfiguration>().ApplySavedFile();

        var settings = (await Backup(a)).Manifest.Settings!;
        Assert.Equal(BackupSettingsState.Included, settings.State);
    }

    [Fact]
    public async Task AMergedBackup_OfAServerWhoseFileTheStartLeftOut_StillCarriesTheEnvironmentsSettings_NotTheFiles()
    {
        // Merge was chosen so that the environment's values travel: a left-out file takes none of its own along, the environment's still go.
        using var a = Host("a", "None", """{ "Notifications": { "MaxPerHour": "0" } }""",
            new() { ["Backups:Environment"] = "Merge", ["Defaults:Currency"] = "XYZ" });
        Assert.True(a.Factory.Services.GetRequiredService<SettingsFileStatus>().Ignored);

        var written = await Backup(a);

        Assert.Equal(BackupSettingsState.Included, written.Manifest.Settings!.State);
        var carried = BackupFile.ReadSettings(written.Path, written.Manifest, null)!;
        Assert.Equal("XYZ", carried["Defaults:Currency"]);
        Assert.DoesNotContain(carried.Keys, k => k.StartsWith("Notifications:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnEncryptedBackup_BringsItsSecretsBack()
    {
        var salt = Encoding.ASCII.GetBytes("tankstat-salt-01");
        var identity = BackupKeys.Identity("correct horse battery staple", salt, Argon2idParameters.Default);
        var encryption = new Dictionary<string, string?>
        {
            ["Backups:Encryption:Recipient"] = AgeKeys.FormatRecipient(BackupKeys.Recipient(identity)),
            ["Backups:Encryption:Salt"] = Convert.ToBase64String(salt),
        };
        string backup;
        using (var a = Host("a", "None", """{ "Smtp": { "Password": "zzzz-a-password" } }""", encryption))
            backup = (await Backup(a)).Path;
        // The same key set in its environment, as the old server's: a backup records what its server's environment set, and a restore
        // where it is missing is refused.
        using var b = Host("b", "None", """{ "Smtp": { "Password": "b-password" } }""", encryption);
        Stage(Folder("b-backups"), backup, AgeKeys.FormatIdentity(identity));

        _ = b.Factory.Services;

        Assert.Equal("zzzz-a-password", (string?)Json(b.SettingsPath)["Smtp"]!["Password"]);
        Assert.False(b.Log.Mentions("zzzz-a-password"));
    }

    [Fact]
    public async Task ABackupRecordsWhatItsServersEnvironmentSet_ByName_NeverAValue()
    {
        using var a = Host("a", "None", file: null, new() { ["Defaults:Currency"] = "ZZZZ" });

        var written = await Backup(a);

        var recorded = written.Manifest.Environment!;
        Assert.Equal(BackupEnvironmentMode.Record, recorded.Mode);
        Assert.Contains("Defaults__Currency", recorded.Settings);
        Assert.Contains("Auth__Mode", recorded.Settings);
        Assert.Contains("Database__ConnectionString", recorded.Server); // the machine's own: named, never carried
        Assert.Contains("Storage__Path", recorded.Server);
        Assert.Equal(BackupSettingsState.None, written.Manifest.Settings!.State); // recorded only: no file here, and none made of the environment
        Assert.DoesNotContain("ZZZZ", written.Manifest.ToJson());
    }

    [Fact]
    public async Task ARestoreWhereASettingTheBackupsServerSetHasNoValue_IsRefused_AndAppliedOnceItIsSet()
    {
        string backup;
        using (var a = Host("a", "None", file: null, new() { ["Defaults:Currency"] = "XYZ" }))
            backup = (await Backup(a)).Path;
        using (var refused = Host("b", "None", file: null))
        {
            Stage(Folder("b-backups"), backup);
            _ = refused.Factory.Services;
            Assert.Single(Directory.GetFiles(Folder("b-backups"), "restore.refused.*"));
            Assert.Contains(refused.Log.Entries, e => e.Values.TryGetValue("Reason", out var r) && (string?)r == "backup.environmentMissing");
        }
        using var b = Host("c", "None", file: null, new() { ["Defaults:Currency"] = "EUR" }); // set: its own value is what runs, as always
        Stage(Folder("c-backups"), backup);

        _ = b.Factory.Services;

        Assert.Empty(Directory.GetFiles(Folder("c-backups"), "restore.refused.*"));
        Assert.False(File.Exists(RestoreMarker.PathIn(Folder("c-backups"))));
    }

    [Fact]
    public async Task ALogLevelTheBackupsServerSetInItsEnvironment_DoesNotRefuseARestoreWhereItHasNoValue()
    {
        // A compose file often sets Logging__LogLevel__Default: a server without it still restores (user decision), only what the log holds differs.
        string backup;
        using (var a = Host("a", "None", file: null, new() { ["Logging:LogLevel:Default"] = "Warning" }))
        {
            var written = await Backup(a);
            Assert.Contains("Logging__LogLevel__Default", written.Manifest.Environment!.Settings);
            backup = written.Path;
        }
        using var b = Host("b", "None", file: null);
        Stage(Folder("b-backups"), backup);

        _ = b.Factory.Services;

        Assert.Empty(Directory.GetFiles(Folder("b-backups"), "restore.refused.*"));
        Assert.False(File.Exists(RestoreMarker.PathIn(Folder("b-backups"))));
    }

    [Fact]
    public async Task AMergedBackup_CarriesTheEnvironmentsSettingsInItsFile_ButNoSecretInPlain_WhichTheRestoredServerMustThenSet()
    {
        string backup;
        using (var a = Host("a", "None", file: null, new() { ["Backups:Environment"] = "Merge", ["Defaults:Currency"] = "XYZ", ["Smtp:Password"] = "zzzz-a-password" }))
        {
            var written = await Backup(a);
            Assert.Equal(BackupSettingsState.Included, written.Manifest.Settings!.State); // a file made of the environment, the server has none
            var carried = BackupFile.ReadSettings(written.Path, written.Manifest, null)!;
            Assert.Equal("XYZ", carried["Defaults:Currency"]);
            Assert.False(carried.ContainsKey("Smtp:Password")); // a plain backup carries no secret, merged or not...
            Assert.Contains("Smtp__Password", written.Manifest.Environment!.Settings); // ...but records it
            Assert.DoesNotContain("zzzz", File.ReadAllText(written.Path));
            backup = written.Path;
        }
        using (var refused = Host("b", "None", file: null, new() { ["Backups:Environment"] = "Merge" }))
        {
            Stage(Folder("b-backups"), backup);
            _ = refused.Factory.Services;
            Assert.Single(Directory.GetFiles(Folder("b-backups"), "restore.refused.*")); // the password: recorded, not carried, not set here
        }
        using var b = Host("c", "None", file: null, new() { ["Backups:Environment"] = "Merge", ["Smtp:Password"] = "c-password" });
        Stage(Folder("c-backups"), backup);

        _ = b.Factory.Services;

        Assert.Equal("XYZ", (string?)Json(b.SettingsPath)["Defaults"]!["Currency"]);
        Assert.Null(Json(b.SettingsPath)["Smtp"]); // the environment's password stays the environment's
    }

    [Fact]
    public async Task TheBackupsPage_AndTheRestoreCommand_SayWhatABackupBrings_AndWhatThisServerLacks()
    {
        string backup;
        var admin = new Dictionary<string, string?> { ["Auth:Standalone:AdminEmail"] = TestApp.AdminEmail, ["Auth:Standalone:AdminPassword"] = TestApp.AdminPassword };
        using (var a = Host("a", "Standalone", """{ "Notifications": { "MaxPerHour": "7" } }""", new(admin) { ["Defaults:Currency"] = "XYZ" }))
            backup = (await Backup(a)).Path;
        using var b = Host("b", "Standalone", file: null, admin);
        Stage(Folder("b-backups"), backup, marker: false);
        using var client = b.NewClient();
        await client.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);

        var listed = (await client.Gql("{ backups { settings { environment } restorePoints { settingsFile { state omitted } environment { mode settings server missing } } } }"))
            .Data().GetProperty("backups");

        Assert.Equal("RECORD", listed.GetProperty("settings").GetProperty("environment").GetString());
        var point = Assert.Single(listed.GetProperty("restorePoints").EnumerateArray());
        Assert.Equal("INCLUDED", point.GetProperty("settingsFile").GetProperty("state").GetString());
        var environment = point.GetProperty("environment");
        Assert.Equal("RECORD", environment.GetProperty("mode").GetString());
        Assert.Contains("Defaults__Currency", environment.GetProperty("settings").EnumerateArray().Select(n => n.GetString()));
        Assert.Contains("Storage__Path", environment.GetProperty("server").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal(["Defaults__Currency"], environment.GetProperty("missing").EnumerateArray().Select(n => n.GetString())); // the rest this host sets too

        var (output, error) = (new StringWriter(), new StringWriter());
        var code = await Tankstat.Api.Backups.BackupCommands.RunAsync(["restore", Path.GetFileName(backup)], b.Factory.Services, new StringReader("no\n"), output, error);
        Assert.Equal(1, code);
        Assert.Contains("The backup's settings file: ", output.ToString());
        Assert.Contains("This server sets none of Defaults__Currency: the restore is refused until they are set.", output.ToString());
        Assert.DoesNotContain("XYZ", output.ToString());
        var refused = await Tankstat.Api.Backups.BackupCommands.RunAsync(["restore", Path.GetFileName(backup), "--yes"], b.Factory.Services, new StringReader(""), output, error);
        Assert.Equal(1, refused);
        Assert.Contains("Defaults__Currency", error.ToString()); // the refusal, before the marker
        Assert.False(File.Exists(RestoreMarker.PathIn(Folder("b-backups"))));
    }

    /// <summary>A backup of an instance without users, whose settings file says Standalone (its sign-in held off by the environment).</summary>
    private async Task<string> StandaloneFileWithoutAnAdministrator()
    {
        using var a = Host("a", "None", """{ "Auth": { "Mode": "Standalone" } }""");
        return (await Backup(a)).Path;
    }

    [Fact]
    public async Task AStandaloneSettingsFile_RestoredOnAServerWithoutCredentials_NeedsAnAdministratorInTheBackup_AndIsRefusedWithEverythingAsItWas()
    {
        var backup = await StandaloneFileWithoutAnAdministrator();
        var bFile = """{ "Auth": { "Mode": "None" } }""";
        using var b = Host("b", mode: null, bFile); // its sign-in from its own file, which the restore would replace
        Stage(Folder("b-backups"), backup);

        _ = b.Factory.Services;

        Assert.Single(Directory.GetFiles(Folder("b-backups"), "restore.refused.*"));
        Assert.Equal(bFile, File.ReadAllText(b.SettingsPath));
        Assert.Equal(0, b.Restarts.NowCount);
        Assert.Contains(b.Log.Entries, e => e.Values.TryGetValue("Reason", out var r) && (string?)r == "settings.standaloneNeedsAdministrator");
    }

    [Fact]
    public async Task AStandaloneSettingsFile_IsRestored_WhereTheBootstrapCredentialsCanMakeTheAdministrator()
    {
        var backup = await StandaloneFileWithoutAnAdministrator();
        using var b = Host("b", mode: null, """{ "Auth": { "Mode": "None" } }""", new()
        {
            ["Auth:Standalone:AdminEmail"] = TestApp.AdminEmail,
            ["Auth:Standalone:AdminPassword"] = TestApp.AdminPassword,
        });
        Stage(Folder("b-backups"), backup);

        _ = b.Factory.Services;

        Assert.Equal("Standalone", (string?)Json(b.SettingsPath)["Auth"]!["Mode"]);
        Assert.Equal(1, b.Restarts.NowCount);
    }

    [Fact]
    public async Task ARestoreRequest_IsRefusedWhenThisServerWouldNotStartWithTheBackupsSettingsFile_AndNothingIsWritten()
    {
        // A complete OIDC sign-in in its file (its environment keeps it at None, so it runs here without the provider); a plain backup leaves
        // the client secret out, and this server has none of its own.
        string backup;
        using (var a = Host("a", "None", """{ "Auth": { "Mode": "Oidc", "Oidc": { "Authority": "https://id.example.com", "ClientId": "tankstat", "ClientSecret": "zzzz-client-secret" } } }"""))
            backup = (await Backup(a)).Path;
        using var b = Host("b", mode: null, """{ "Auth": { "Mode": "Standalone", "Standalone": { "AdminEmail": "root@example.com", "AdminPassword": "initial-password-1" } } }""");
        Stage(Folder("b-backups"), backup, marker: false);
        using var client = b.NewClient();
        await client.LoginAs(TestApp.AdminEmail, TestApp.AdminPassword);

        var refused = await client.Gql(Restore, new { i = new { file = Path.GetFileName(backup) } });

        Assert.Equal("backup.settingsRefused", refused.ErrorKey()); // Oidc without its client secret: the next start would stop
        Assert.False(File.Exists(RestoreMarker.PathIn(Folder("b-backups"))));
        Assert.Equal(0, b.Restarts.Count);
    }
}

/// <summary>Tests that must not run beside any other (a host's settings depend on the process's environment, which <see cref="ApiFixture"/> changes): after the others, one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AloneCollection
{
    public const string Name = "Alone";
}
