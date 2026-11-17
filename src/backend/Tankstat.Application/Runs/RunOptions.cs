using Microsoft.Extensions.Options;

namespace Tankstat.Application.Runs;

/// <summary>
/// Bound from the "Runs" section (<c>Runs__LimitMinutes=10</c>): the background runs that save in one transaction (#335: an import from a
/// backup, a partial restore).
/// </summary>
public sealed class RunOptions
{
    public const string SectionName = "Runs";
    public const int MinLimitMinutes = 1;
    public const int MaxLimitMinutes = 24 * 60;

    /// <summary>How long a run may hold its transaction; past it the run is rolled back and says so. Also how old an open mark may be before it stops making saves wait.</summary>
    public int LimitMinutes { get; set; } = 10;

    public TimeSpan Limit => TimeSpan.FromMinutes(LimitMinutes);
}

internal sealed class RunOptionsValidator : IValidateOptions<RunOptions>
{
    public ValidateOptionsResult Validate(string? name, RunOptions o)
    {
        if (o.LimitMinutes is < RunOptions.MinLimitMinutes or > RunOptions.MaxLimitMinutes)
            return ValidateOptionsResult.Fail($"Runs:LimitMinutes must be between {RunOptions.MinLimitMinutes} and {RunOptions.MaxLimitMinutes}.");
        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// The two waits around a run that are no settings: fixed for the app, shortened by tests (which have no saves in flight to wait out).
/// </summary>
public sealed class RunTimings
{
    /// <summary>How long someone else's save waits for a run that holds its car before it is refused as busy (the owner's "about 10 seconds").</summary>
    public TimeSpan BusyWait { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The pause between a run's mark being saved and its transaction opening: saves that passed the gate just before the mark finish, and
    /// another server's gate (which looks at the marks at most once in <see cref="RunGate.RecheckAfter"/>) sees the mark.
    /// </summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(2);
}
