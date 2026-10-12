using Microsoft.Extensions.Configuration;
using Tankstat.Infrastructure.Settings;

namespace Tankstat.Infrastructure.UnitTests;

public sealed class SettingsFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tankstat-settings-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task NoFile_ReadsAsNothing()
    {
        var store = new SettingsFileStore(FilePath);

        Assert.Empty(await store.ReadAsync(default));
        Assert.Equal(FilePath, store.Path);
    }

    [Fact]
    public async Task WritesNestedJson_WithListsAsArrays_AndReadsTheSameKeysBack()
    {
        var store = new SettingsFileStore(FilePath); // the folder does not exist yet
        var values = new Dictionary<string, string?>
        {
            ["Auth:Mode"] = "Standalone",
            ["Auth:AdminEmails:0"] = "a@example.com",
            ["Auth:AdminEmails:1"] = "b@example.com",
            ["Smtp:Port"] = "587",
            ["Smtp:Password"] = "hunter2-zzzz",
            ["Defaults:Currency"] = "HUF",
            ["Gone"] = null, // a null is left out, not written as null
            ["Odd::Key"] = "x", // an empty segment cannot be a key
        };

        await store.WriteAsync(values, default);

        var text = await File.ReadAllTextAsync(FilePath);
        Assert.Contains("\"AdminEmails\": [", text);
        Assert.Contains("\"a@example.com\",", text);
        Assert.Contains("\"Port\": \"587\"", text); // every value a string, as the configuration reads it
        Assert.DoesNotContain("Gone", text);
        Assert.DoesNotContain("Odd", text);
        var read = await store.ReadAsync(default);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["Auth:Mode"] = "Standalone",
                ["Auth:AdminEmails:0"] = "a@example.com",
                ["Auth:AdminEmails:1"] = "b@example.com",
                ["Smtp:Port"] = "587",
                ["Smtp:Password"] = "hunter2-zzzz",
                ["Defaults:Currency"] = "HUF",
            }.OrderBy(p => p.Key),
            read.OrderBy(p => p.Key));
        Assert.True(read.ContainsKey("auth:mode")); // keys as the configuration: case does not matter
        Assert.False(File.Exists(store.BackupPath)); // nothing was there before
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task AFileWhoseNameStartsWithADot_IsReadLikeAnyOther()
    {
        var store = new SettingsFileStore(Path.Combine(_folder, ".settings.json")); // the provider AddJsonFile makes would hide it and call it missing
        await store.WriteAsync(new Dictionary<string, string?> { ["Defaults:Currency"] = "HUF" }, default);

        Assert.Equal("HUF", (await store.ReadAsync(default))["Defaults:Currency"]);
        Assert.Equal("HUF", new ConfigurationBuilder().Add(SettingsFileStore.SourceFor(store.Path)).Build()["Defaults:Currency"]); // the source the app starts with
    }

    [Fact]
    public async Task TheSecondWrite_KeepsThePreviousFile_AsTheBackup()
    {
        var store = new SettingsFileStore(FilePath);
        await store.WriteAsync(new Dictionary<string, string?> { ["Defaults:Currency"] = "HUF" }, default);

        await store.WriteAsync(new Dictionary<string, string?> { ["Defaults:Currency"] = "USD" }, default);

        Assert.Equal("USD", (await store.ReadAsync(default))["Defaults:Currency"]);
        Assert.Equal("HUF", SettingsFileStore.Read(store.BackupPath)["Defaults:Currency"]);
    }

    [Fact]
    public async Task TheFileAndItsBackup_AreReadableByTheOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return; // Unix file modes
        var store = new SettingsFileStore(FilePath);
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(FilePath, "{ \"Defaults\": { \"Currency\": \"HUF\" } }"); // a file an operator wrote by hand, world-readable
        File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await store.WriteAsync(new Dictionary<string, string?> { ["Smtp:Password"] = "hunter2-zzzz" }, default);

        Assert.Equal(SettingsFileStore.OwnerOnly, File.GetUnixFileMode(FilePath));
        Assert.Equal(SettingsFileStore.OwnerOnly, File.GetUnixFileMode(store.BackupPath));
    }

    [Fact]
    public void AHandWrittenFile_ReadsNumbersAndBooleansAsText_AndObjectsNotNumberedFromZeroAsObjects()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "Smtp": { "Port": 587 }, "Auth": { "Standalone": { "AllowAdminSetPassword": true } }, "Odd": { "1": "a", "2": "b" } }""");

        var read = SettingsFileStore.Read(FilePath);

        Assert.Equal("587", read["Smtp:Port"]);
        Assert.Equal("True", read["Auth:Standalone:AllowAdminSetPassword"]); // the JSON provider's spelling; the binder reads it either way
        Assert.Equal("a", read["Odd:1"]);
        Assert.Contains("\"1\": \"a\"", SettingsFileStore.ToJson(read)); // written back as the object it was, not shifted into an array
    }

    [Fact]
    public void ToJson_IsStableAndIndented()
    {
        var json = SettingsFileStore.ToJson(new Dictionary<string, string?> { ["B:Y"] = "2", ["A"] = "1", ["B:X"] = "1" });

        Assert.Equal("{\n  \"A\": \"1\",\n  \"B\": {\n    \"X\": \"1\",\n    \"Y\": \"2\"\n  }\n}\n", json.Replace("\r\n", "\n"));
    }
}
