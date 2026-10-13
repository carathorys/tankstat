namespace Tankstat.Domain.Settings;

/// <summary>How times of day are written: on a 24-hour clock (13:29) or a 12-hour one (1:29 PM). Unset, as the UI language writes them.</summary>
public enum TimeFormat
{
    TwentyFourHour,
    TwelveHour,
}
