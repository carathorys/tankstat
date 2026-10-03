using System.Text.RegularExpressions;

namespace Tankstat.Reader.Core.Text;

/// <summary>A date printed on a receipt, where it is in the text, and whether it could be meant otherwise (02/03 day-first or month-first).</summary>
public sealed record DateMatch(DateOnly Date, int Index, int Length);

/// <summary>
/// Dates in the formats Hungarian, German and English receipts use: "2026.10.02." and "2026-10-02" (year first), "02.10.2026"
/// (day first), "02/10/2026" (day first in Europe, month first in the US: both are offered when both are valid). Only dates from three
/// years back to tomorrow count: a receipt is never from the future, and tomorrow covers every time zone.
/// </summary>
public static partial class Dates
{
    [GeneratedRegex(@"(?<!\d)(?<y>20\d{2})\s?[.\-/]\s?(?<m>\d{1,2})\s?[.\-/]\s?(?<d>\d{1,2})\.?(?!\d)")]
    private static partial Regex YearFirst();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})\s?\.\s?(?<m>\d{1,2})\s?\.\s?(?<y>(?:20)?\d{2})(?!\d)")]
    private static partial Regex DayFirstDots();

    [GeneratedRegex(@"(?<!\d)(?<a>\d{1,2})[/\-](?<b>\d{1,2})[/\-](?<y>(?:20)?\d{2})(?!\d)")]
    private static partial Regex Slashed();

    [GeneratedRegex(@"(?<!\d)([01]?\d|2[0-3]):[0-5]\d(:[0-5]\d)?(?!\d)")]
    private static partial Regex TimePattern();

    public static IReadOnlyList<DateMatch> Find(string text, DateOnly today)
    {
        var found = new List<DateMatch>();
        foreach (Match m in YearFirst().Matches(text)) Add(found, m, Year(m, "y"), Int(m, "m"), Int(m, "d"), today);
        foreach (Match m in DayFirstDots().Matches(text))
            if (!Overlaps(found, m)) Add(found, m, Year(m, "y"), Int(m, "m"), Int(m, "d"), today);
        foreach (Match m in Slashed().Matches(text))
        {
            if (Overlaps(found, m)) continue;
            Add(found, m, Year(m, "y"), Int(m, "b"), Int(m, "a"), today); // day first (Europe, the UK)
            Add(found, m, Year(m, "y"), Int(m, "a"), Int(m, "b"), today); // month first (the US)
        }
        return found;
    }

    /// <summary>Whether the text holds a time of day ("18:15"), which receipts print next to the date.</summary>
    public static bool HasTime(string text) => TimePattern().IsMatch(text);

    /// <summary>Where the times of day are, so their digits are not taken for amounts.</summary>
    public static IEnumerable<(int Index, int Length)> FindTimes(string text) => TimePattern().Matches(text).Select(m => (m.Index, m.Length));

    private static void Add(List<DateMatch> found, Match m, int year, int month, int day, DateOnly today)
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return;
        var date = new DateOnly(year, month, day);
        if (date > today.AddDays(1) || date < today.AddYears(-3)) return;
        if (found.Any(f => f.Date == date && f.Index == m.Index)) return;
        found.Add(new DateMatch(date, m.Index, m.Length));
    }

    private static bool Overlaps(List<DateMatch> found, Match m) => found.Any(f => m.Index < f.Index + f.Length && f.Index < m.Index + m.Length);
    private static int Int(Match m, string group) => int.Parse(m.Groups[group].Value);
    private static int Year(Match m, string group) => Int(m, group) is var y && y < 100 ? 2000 + y : y;
}
