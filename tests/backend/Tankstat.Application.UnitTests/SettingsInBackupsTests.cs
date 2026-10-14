using System.Text.Json;
using Tankstat.Application.Backups;

namespace Tankstat.Application.UnitTests;

/// <summary>A backup carries the instance's settings, never the machine's, and a plain one never a secret; a restore keeps this machine's own.</summary>
public sealed class SettingsInBackupsTests
{
    private static readonly Dictionary<string, string?> File = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Auth:Mode"] = "Standalone",
        ["Auth:AdminEmails:0"] = "a@x.co",
        ["Auth:AdminEmails:1"] = "b@x.co",
        ["Smtp:Password"] = "zzzz-secret",
        ["Defaults:Currency"] = "HUF",
        ["Logging:Console:FormatterName"] = "json", // the Logging section, beyond the five levels the page offers
        ["Storage:Path"] = "/srv/pictures", // the machine's: a folder
        ["Database:ConnectionString"] = "Host=db", // the machine's
        ["Auth:Standalone:AdminPassword"] = "bootstrap-password", // left out of the page on purpose
        ["Backups:Path"] = "/srv/backups",
        ["Setup:Enabled"] = "false", // the environment's only
    };

    [Fact]
    public void APlainBackup_CarriesTheInstanceSettings_WithoutTheSecrets_AndNamesWhatItLeavesOut()
    {
        var (carried, omitted) = SettingsInBackups.Copy(File, encrypted: false);

        Assert.Equal(["Auth:AdminEmails:0", "Auth:AdminEmails:1", "Auth:Mode", "Defaults:Currency", "Logging:Console:FormatterName"], carried.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Auth:Standalone:AdminPassword", "Backups:Path", "Database:ConnectionString", "Setup:Enabled", "Smtp:Password", "Storage:Path"], omitted);
        Assert.DoesNotContain("zzzz-secret", JsonSerializer.Serialize(omitted)); // names only
    }

    [Fact]
    public void AnEncryptedBackup_CarriesTheSecretsToo_NeverTheMachinesOwn()
    {
        var (carried, omitted) = SettingsInBackups.Copy(File, encrypted: true);

        Assert.Equal("zzzz-secret", carried["Smtp:Password"]);
        Assert.DoesNotContain("Smtp:Password", omitted);
        Assert.Contains("Storage:Path", omitted);
        Assert.False(carried.ContainsKey("Auth:Standalone:AdminPassword"));
    }

    [Fact]
    public void ARestore_LeavesTheBackupsSettings_AndKeepsThisMachinesOwn_AndThePasswordsAPlainBackupCouldNotBring()
    {
        var live = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Auth:Mode"] = "None", // the backup's wins
            ["Defaults:Currency"] = "EUR", // the backup has none: gone with the rest of this server's file
            ["Smtp:Password"] = "this-servers-password",
            ["Storage:Path"] = "/data/uploads",
        };
        var fromBackup = new Dictionary<string, string?> { ["Auth:Mode"] = "Standalone", ["Database:ConnectionString"] = "Host=elsewhere" }; // a key no backup carries: not taken

        var restored = SettingsInBackups.Restored(fromBackup, live, encrypted: false);

        Assert.Equal(new Dictionary<string, string?> { ["Auth:Mode"] = "Standalone", ["Smtp:Password"] = "this-servers-password", ["Storage:Path"] = "/data/uploads" },
            restored.OrderBy(p => p.Key).ToDictionary());
    }

    [Fact]
    public void ARestoreOfAnEncryptedBackup_TakesItsSecrets_EvenWhereItHasNone()
    {
        var live = new Dictionary<string, string?> { ["Smtp:Password"] = "this-servers-password" };

        var restored = SettingsInBackups.Restored(new Dictionary<string, string?>(), live, encrypted: true);

        Assert.Empty(restored); // an encrypted backup carries the secrets it had: none here, so none stays
    }

    [Fact]
    public void TheManifest_ReadsWithAndWithoutTheSettingsMember()
    {
        var manifest = new BackupManifest(
            BackupManifest.CurrentFormat, BackupManifest.Tankstat, "1.0.0", DateTimeOffset.UnixEpoch, "Sqlite", null, false,
            new BackupContents(new Dictionary<string, long>(), true, BackupPhotos.WithPictures, false), new BackupSizes(0, 0, 0, 0, 0), new BackupArchive(BackupArchive.Plain, "ab"), null,
            new BackupSettingsFile(BackupSettingsState.Included, BackupSettingsFile.Plain, "cd", 12, ["Smtp:Password"]));
        var json = manifest.ToJson();

        Assert.Contains("\"state\": \"included\"", json);
        Assert.Equal(["Smtp:Password"], BackupManifest.FromJson(json).Settings!.Omitted);
        var older = json[..json.IndexOf(",\n  \"settings\"", StringComparison.Ordinal)] + "\n}"; // as a release before it wrote them
        Assert.Null(BackupManifest.FromJson(older).Settings);
    }
}
