using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Exports;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The exports' folder (#341) stands apart from what a backup copies or writes; a file is kept from 1 to 720 hours.</summary>
public class ExportOptionsTests
{
    private static readonly string Data = Path.Combine(Path.GetTempPath(), "tankstat-export-options");

    private static ExportOptions Bound(params (string Key, string? Value)[] settings)
    {
        var all = new Dictionary<string, string?> { ["Storage:Path"] = Path.Combine(Data, "uploads"), ["Backups:Path"] = Path.Combine(Data, "backups") };
        foreach (var (key, value) in settings) all[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(all).Build();
        return new ServiceCollection().AddLogging().AddApplication(config).BuildServiceProvider().GetRequiredService<IOptions<ExportOptions>>().Value;
    }

    private static string Refusal(params (string Key, string? Value)[] settings) =>
        string.Join("\n", Assert.Throws<OptionsValidationException>(() => Bound(settings)).Failures);

    [Fact]
    public void Defaults_AreAnExportsFolderInTheWorkingDirectory_On_KeptADay()
    {
        var options = Bound();
        Assert.Equal((Path.GetFullPath("exports"), true, 24), (options.Root, options.Enabled, options.KeepHours));
    }

    [Theory]
    [InlineData("uploads", "Storage:Path")]
    [InlineData("uploads/exports", "Storage:Path")]
    [InlineData("backups", "Backups:Path")]
    [InlineData("backups/exports", "Backups:Path")]
    [InlineData(".", "Storage:Path")]
    public void TheFolder_IsNeverOneABackupCopiesOrWrites(string folder, string other) =>
        Assert.Contains($"Exports:Folder and {other}", Refusal(("Exports:Folder", Path.Combine(Data, folder))));

    [Theory]
    [InlineData("0")]
    [InlineData("721")]
    public void KeepHours_IsFrom1To720(string hours) => Assert.Contains("Exports:KeepHours", Refusal(("Exports:KeepHours", hours)));

    [Fact]
    public void AFolderOfItsOwn_AndTheLimits_AreTaken() =>
        Assert.Equal(720, Bound(("Exports:Folder", Path.Combine(Data, "exports")), ("Exports:KeepHours", "720")).KeepHours);
}
