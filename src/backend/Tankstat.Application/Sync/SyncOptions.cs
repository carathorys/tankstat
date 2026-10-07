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
}

public sealed class SyncOptionsValidator : IValidateOptions<SyncOptions>
{
    public ValidateOptionsResult Validate(string? name, SyncOptions o) =>
        o.TombstoneRetentionDays is >= 1 and <= 3650
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Sync:TombstoneRetentionDays must be between 1 and 3650.");
}
