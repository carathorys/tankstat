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

    /// <summary>
    /// How many days a notification is kept after it was read; then the system removes it (users cannot delete notifications). Unread ones
    /// are kept. A reminder of a schedule that is still due stays, so it does not come back as new.
    /// </summary>
    public int ReadRetentionDays { get; set; } = 30;
}

public sealed class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationOptions o)
    {
        var errors = new List<string>();
        if (o.MaxPerHour < 1) errors.Add("Notifications:MaxPerHour must be at least 1.");
        if (o.ReadRetentionDays < 1) errors.Add("Notifications:ReadRetentionDays must be at least 1.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
