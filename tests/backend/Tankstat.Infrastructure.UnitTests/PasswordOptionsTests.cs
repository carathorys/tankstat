using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Users;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class PasswordOptionsTests
{
    private static ServiceProvider Services(Dictionary<string, string?> values) =>
        new ServiceCollection().AddLogging().AddApplication(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

    [Fact]
    public void NothingConfigured_RequiresTenCharacters_TheRuleBeforeTheSettingExisted()
    {
        using var services = Services([]);

        Assert.Equal((10, 128), (services.GetRequiredService<PasswordRequirements>().MinLength, PasswordRequirements.MaxLength));
    }

    [Fact]
    public void TheMinimum_IsBoundBesideTheSignIn_AndReadAfresh()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Standalone:PasswordMinLength"] = "14", ["Auth:Standalone:MaxFailedAttempts"] = "3" });
        using var services = new ServiceCollection().AddLogging().AddApplication(config).BuildServiceProvider();
        var requirements = services.GetRequiredService<PasswordRequirements>();
        Assert.Equal(14, requirements.MinLength);
        Assert.Equal(3, services.GetRequiredService<IOptions<Tankstat.Application.Auth.AuthOptions>>().Value.Standalone.MaxFailedAttempts); // the sign-in binds beside it

        // What a save on the settings page does: the file's value changes and the configuration reloads.
        config["Auth:Standalone:PasswordMinLength"] = "20";
        ((IConfigurationRoot)config).Reload();

        Assert.Equal(20, requirements.MinLength);
    }

    [Fact]
    public void AMinimumThatStopsBeingUsableWhileTheAppRuns_KeepsTheLastUsableOne_SaidOnce_UntilItIsFixed()
    {
        // An appsettings.json edited by hand reloads on its own (the settings page never saves such a value): every read of the options
        // would throw, and with it every sign-in, password change and reset.
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Standalone:PasswordMinLength"] = "14" });
        var log = new CapturedLog();
        using var services = new ServiceCollection().AddLogging(b => b.AddProvider(log)).AddApplication(config).BuildServiceProvider();
        var requirements = services.GetRequiredService<PasswordRequirements>();

        foreach (var unusable in new[] { "fourteen", "8" }) // one the binder cannot read, one the validator refuses
        {
            config["Auth:Standalone:PasswordMinLength"] = unusable;
            // The options monitor reads the options while the reload is told, so whoever reloads gets their failure: here the test, in the app
            // the file watcher's own task, which drops it. What is read afterwards is what counts.
            Assert.ThrowsAny<Exception>(() => ((IConfigurationRoot)config).Reload());

            Assert.Equal(14, requirements.MinLength); // the last usable one, not the floor: a mistake never lowers a raised minimum
            Assert.True(requirements.Meets("fourteen-chars"));
            Assert.False(requirements.Meets("thirteen-char"));
        }
        var warning = Assert.Single(log.From<PasswordRequirements>()); // once for the whole spell
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal((object?)14, warning.Values["MinLength"]); // it names the minimum kept in force, never the floor
        Assert.DoesNotContain("fourteen", warning.Text); // the binder's words quote the value

        config["Auth:Standalone:PasswordMinLength"] = "16";
        ((IConfigurationRoot)config).Reload();

        Assert.Equal(16, requirements.MinLength);
        Assert.Equal([LogLevel.Warning, LogLevel.Information], log.From<PasswordRequirements>().Select(e => e.Level));
    }

    [Theory]
    [InlineData("9")] // below the floor
    [InlineData("129")] // above the ceiling
    public void AMinimumOutsideTheFloorAndTheCeiling_FailsValidation(string value)
    {
        using var services = Services(new() { ["Auth:Standalone:PasswordMinLength"] = value });

        var failed = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptionsMonitor<PasswordOptions>>().CurrentValue);
        Assert.StartsWith("Auth:Standalone:PasswordMinLength", failed.Failures.Single()); // the settings page says it at the field
    }
}
