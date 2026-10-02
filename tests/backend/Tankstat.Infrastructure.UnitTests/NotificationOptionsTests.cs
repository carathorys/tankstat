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
    public void NothingConfigured_AllowsTwentyAnHour() => Assert.Equal(20, Resolve([]).MaxPerHour);

    [Fact]
    public void TheHourlyLimit_CanBeConfigured() => Assert.Equal(50, Resolve(new() { ["Notifications:MaxPerHour"] = "50" }).MaxPerHour);

    [Fact]
    public void ALimitBelowOne_FailsValidation() =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Notifications:MaxPerHour"] = "0" }));
}
