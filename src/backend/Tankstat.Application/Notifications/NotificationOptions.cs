using Microsoft.Extensions.Options;

namespace Tankstat.Application.Notifications;

/// <summary>Bound from the "Notifications" section (e.g. <c>Notifications__MaxPerHour=50</c>).</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// How many notifications about events (such as access changes) a user gets in an hour before further ones are only counted in a
    /// single "more activity" notification. Recurring-expense reminders do not count.
    /// </summary>
    public int MaxPerHour { get; set; } = 20;
}

public sealed class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationOptions o) =>
        o.MaxPerHour < 1 ? ValidateOptionsResult.Fail("Notifications:MaxPerHour must be at least 1.") : ValidateOptionsResult.Success;
}
