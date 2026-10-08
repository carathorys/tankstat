using Microsoft.Extensions.Options;

namespace Tankstat.Application.Sync;

/// <summary>Settings of what devices download for offline use (<c>Sync</c> section).</summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// How many days the server remembers what was removed for good (tombstones). A device that last downloaded a vehicle longer ago than
    /// that is told to download it afresh.
    /// </summary>
    public int TombstoneRetentionDays { get; set; } = 90;

    /// <summary>How many days the server remembers the changes it applied (so a device sending a batch again gets the same answer); parked ones stay.</summary>
    public int RetentionDays { get; set; } = 30;
}

public sealed class SyncOptionsValidator : IValidateOptions<SyncOptions>
{
    public ValidateOptionsResult Validate(string? name, SyncOptions o)
    {
        var errors = new List<string>();
        if (o.TombstoneRetentionDays is < 1 or > 3650) errors.Add("Sync:TombstoneRetentionDays must be between 1 and 3650.");
        if (o.RetentionDays is < 1 or > 3650) errors.Add("Sync:RetentionDays must be between 1 and 3650.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
