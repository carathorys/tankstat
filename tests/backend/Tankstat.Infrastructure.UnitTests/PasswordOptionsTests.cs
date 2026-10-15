using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Users;

namespace Tankstat.Infrastructure.UnitTests;

public class PasswordOptionsTests
{
    private static ServiceProvider Services(Dictionary<string, string?> values) =>
        new ServiceCollection().AddApplication(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

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
        using var services = new ServiceCollection().AddApplication(config).BuildServiceProvider();
        var requirements = services.GetRequiredService<PasswordRequirements>();
        Assert.Equal(14, requirements.MinLength);
        Assert.Equal(3, services.GetRequiredService<IOptions<Tankstat.Application.Auth.AuthOptions>>().Value.Standalone.MaxFailedAttempts); // the sign-in binds beside it

        // What a save on the settings page does: the file's value changes and the configuration reloads.
        config["Auth:Standalone:PasswordMinLength"] = "20";
        ((IConfigurationRoot)config).Reload();

        Assert.Equal(20, requirements.MinLength);
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
