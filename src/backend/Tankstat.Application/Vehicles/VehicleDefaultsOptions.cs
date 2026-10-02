using Microsoft.Extensions.Options;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recurring;

namespace Tankstat.Application.Vehicles;

/// <summary>
/// Bound from the "Defaults" section (e.g. <c>Defaults__Currency=USD</c>): the units suggested when a vehicle is created and the
/// reminders a new recurring expense starts with. Every vehicle and schedule keeps its own values; this only pre-fills the forms for the
/// country the instance is used in.
/// </summary>
public sealed class VehicleDefaultsOptions
{
    public const string SectionName = "Defaults";

    public DistanceUnit DistanceUnit { get; set; } = DistanceUnit.Kilometers;
    public VolumeUnit VolumeUnit { get; set; } = VolumeUnit.Liters;
    public string Currency { get; set; } = "EUR";

    /// <summary>How many days before it is due a new recurring expense starts warning (<c>Defaults__RecurringWarnDays</c>).</summary>
    public int RecurringWarnDays { get; set; } = 30;

    /// <summary>How far (in the vehicle's distance unit) before it is due a new recurring expense starts warning (<c>Defaults__RecurringWarnDistance</c>).</summary>
    public long RecurringWarnDistance { get; set; } = 500;
}

public sealed class VehicleDefaultsOptionsValidator : IValidateOptions<VehicleDefaultsOptions>
{
    public ValidateOptionsResult Validate(string? name, VehicleDefaultsOptions o)
    {
        var errors = new List<string>();
        if (o.RecurringWarnDays is < 0 or > RecurringExpense.MaxWarnDays)
            errors.Add($"Defaults:RecurringWarnDays must be between 0 and {RecurringExpense.MaxWarnDays}.");
        if (o.RecurringWarnDistance < 0) errors.Add("Defaults:RecurringWarnDistance cannot be negative.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
