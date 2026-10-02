using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Notifications;

namespace Tankstat.Infrastructure.UnitTests;

public class NotificationOptionsTests
{
    private static NotificationOptions Resolve(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddApplication(config).BuildServiceProvider().GetRequiredService<IOptions<NotificationOptions>>().Value;
    }

    [Fact]
    public void NothingConfigured_AllowsTwentyAnHour_AndKeepsReadOnesThirtyDays()
    {
        var options = Resolve([]);

        Assert.Equal((20, 30), (options.MaxPerHour, options.ReadRetentionDays));
    }

    [Fact]
    public void TheLimitAndTheRetention_CanBeConfigured()
    {
        var options = Resolve(new() { ["Notifications:MaxPerHour"] = "50", ["Notifications:ReadRetentionDays"] = "7" });

        Assert.Equal((50, 7), (options.MaxPerHour, options.ReadRetentionDays));
    }

    [Theory]
    [InlineData("Notifications:MaxPerHour")]
    [InlineData("Notifications:ReadRetentionDays")]
    public void ValuesBelowOne_FailValidation(string key) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { [key] = "0" }));
}
