using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Backups;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The backups folder must stand apart from the storage roots (a backup takes those as a whole), and the encryption settings come as a pair.</summary>
public class BackupOptionsTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "tankstat-backup-options");
    private const string Recipient = "age1a42zkrtucpm5gkzw4ur7759dutj838g6xaj6hafkkyp5002g4p4qzu53nv";
    private const string Salt = "dGFua3N0YXQtc2FsdC0wMQ=="; // "tankstat-salt-01"

    private static Func<BackupOptions> Bound(params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value)).Build();
        var services = new ServiceCollection().AddLogging().AddApplication(config).BuildServiceProvider();
        return () => services.GetRequiredService<IOptions<BackupOptions>>().Value;
    }

    private static string Refusal(params (string Key, string? Value)[] settings) =>
        string.Join("\n", Assert.Throws<OptionsValidationException>(() => Bound(settings)()).Failures);

    [Fact]
    public void Defaults_AreABackupsFolderInTheWorkingDirectory_NoPhotos_NoEncryption()
    {
        var options = Bound()();

        Assert.Equal(Path.GetFullPath("backups"), options.Root);
        Assert.False(options.Photos);
        Assert.False(options.Encrypted);
    }

    [Theory]
    [InlineData("uploads", "Storage:Path")] // the pictures root itself
    [InlineData("uploads/backups", "Storage:Path")] // inside it
    [InlineData("photos/sub", "Storage:PhotosPath")] // inside the photos root
    [InlineData(".", "Storage:Path")] // holding both roots
    public void AFolderThatOverlapsAStorageRoot_IsRefused_NamingBothSettings(string backups, string setting)
    {
        var refusal = Refusal(
            ("Storage:Path", Path.Combine(Data, "uploads")),
            ("Storage:PhotosPath", Path.Combine(Data, "photos")),
            ("Backups:Path", Path.Combine(Data, backups)));

        Assert.Contains("Backups:Path", refusal);
        Assert.Contains(setting, refusal);
    }

    [Fact]
    public void AFolderNextToTheRoots_IsFine_AndACommonPrefixIsNotNesting()
    {
        var options = Bound(("Storage:Path", Path.Combine(Data, "uploads")), ("Backups:Path", Path.Combine(Data, "uploads-backups")))();

        Assert.Equal(Path.Combine(Data, "uploads-backups"), options.Root);
    }

    [Fact]
    public void TheEncryptionSettings_ComeAsAPair_AndMustBeARecipientAndASalt()
    {
        Assert.Contains("set together", Refusal(("Backups:Encryption:Recipient", Recipient)));
        Assert.Contains("set together", Refusal(("Backups:Encryption:Salt", Salt)));
        Assert.Contains("not an age recipient", Refusal(("Backups:Encryption:Recipient", "AGE-SECRET-KEY-1RR0SC0HNJ9QQW4A56Z4XYHLYYQP6K89KR2SFUGJGZT2G7926RSKSZAXXRC"), ("Backups:Encryption:Salt", Salt)));
        Assert.Contains("base64", Refusal(("Backups:Encryption:Recipient", Recipient), ("Backups:Encryption:Salt", "not base64!")));
        Assert.Contains("base64", Refusal(("Backups:Encryption:Recipient", Recipient), ("Backups:Encryption:Salt", "AAAA"))); // 3 bytes

        var options = Bound(("Backups:Encryption:Recipient", Recipient), ("Backups:Encryption:Salt", Salt))();
        Assert.True(options.Encrypted);
    }

    [Fact]
    public void AnEmptyPath_IsRefused()
    {
        Assert.Contains("Backups:Path must not be empty", Refusal(("Backups:Path", " ")));
    }

    [Fact]
    public void TheSchedule_IsATimeOfDay_AndRetentionKeepsAtLeastOne_NothingOfItByDefault()
    {
        var defaults = Bound()();
        Assert.Equal((null, null, null, false), (defaults.DailyAt, defaults.Keep, defaults.KeepDays, defaults.Pruning));

        var set = Bound(("Backups:Every", "2:30"), ("Backups:Keep", "14"), ("Backups:KeepDays", "30"))();
        Assert.Equal((new TimeOnly(2, 30), 14, 30, true), (set.DailyAt, set.Keep, set.KeepDays, set.Pruning));
        Assert.Null(Bound(("Backups:Every", ""))().DailyAt); // empty: nothing scheduled, as unset

        foreach (var wrong in new[] { "25:00", "2am", "02:00:00", "daily" })
            Assert.Contains("Backups:Every must be a time of day", Refusal(("Backups:Every", wrong)));
        Assert.Contains("Backups:Keep must be at least 1", Refusal(("Backups:Keep", "0")));
        Assert.Contains("Backups:KeepDays must be at least 1", Refusal(("Backups:KeepDays", "-3")));
    }
}
