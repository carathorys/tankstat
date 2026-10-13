namespace Tankstat.Domain.Settings;

/// <summary>How dates are written, in numbers (unset: as the UI language writes them, with the month's name).</summary>
public enum DateFormat
{
    /// <summary>2026-10-13</summary>
    Iso,
    /// <summary>13.10.2026</summary>
    DayMonthYearDots,
    /// <summary>13/10/2026</summary>
    DayMonthYearSlashes,
    /// <summary>10/13/2026</summary>
    MonthDayYear,
    /// <summary>2026. 10. 13.</summary>
    YearMonthDayDots,
}
