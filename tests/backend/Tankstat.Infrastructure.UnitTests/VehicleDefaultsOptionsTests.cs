using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Vehicles;

namespace Tankstat.Infrastructure.UnitTests;

public class VehicleDefaultsOptionsTests
{
    private static VehicleDefaultsOptions Resolve(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var sp = new ServiceCollection().AddApplication(config).BuildServiceProvider();
        return sp.GetRequiredService<IOptions<VehicleDefaultsOptions>>().Value;
    }

    [Fact]
    public void NothingConfigured_WarnsThirtyDaysAndFiveHundredUnitsBefore()
    {
        var options = Resolve([]);

        Assert.Equal((30, 500L), (options.RecurringWarnDays, options.RecurringWarnDistance));
    }

    [Fact]
    public void TheRecurringWarnings_CanBeConfigured()
    {
        var options = Resolve(new() { ["Defaults:RecurringWarnDays"] = "14", ["Defaults:RecurringWarnDistance"] = "1000" });

        Assert.Equal((14, 1000L), (options.RecurringWarnDays, options.RecurringWarnDistance));
    }

    [Theory]
    [InlineData("Defaults:RecurringWarnDays", "-1")]
    [InlineData("Defaults:RecurringWarnDays", "366")]
    [InlineData("Defaults:RecurringWarnDistance", "-5")]
    public void OutOfRangeWarnings_FailValidation(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { [key] = value }));
}
