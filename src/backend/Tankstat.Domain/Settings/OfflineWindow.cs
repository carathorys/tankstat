using System.Globalization;
using System.Text.RegularExpressions;

namespace Tankstat.Domain.Settings;

/// <summary>
/// Which logs a device downloads for offline use, as a rule it turns into a start date on its own clock at every download (so time zones
/// stay on the device): <c>none</c> (only the vehicle and its schedules), <c>all</c>, <c>thisYear</c>, <c>thisAndLastYear</c>,
/// <c>from:yyyy-MM-dd</c>, or <c>span:</c> an ISO 8601 duration back from now (<c>span:P2Y6M4DT2H48M12S</c>: years, months, days, hours,
/// minutes and seconds in any combination, at least one above zero, at most 100 years). The server only checks the shape.
/// </summary>
public static partial class OfflineWindow
{
    /// <summary>Without a choice: the last two months.</summary>
    public const string Default = "span:P2M";

    /// <summary>Long enough for every valid rule.</summary>
    public const int MaxLength = 40;

    private const double MaxDays = 100 * 365.25;

    /// <summary>The rule, trimmed, or a <c>settings.offlineWindowInvalid</c> error.</summary>
    public static string Require(string? rule)
    {
        var value = rule?.Trim() ?? "";
        if (IsValid(value)) return value;
        throw new DomainException("settings.offlineWindowInvalid", "The offline window must be none, all, thisYear, thisAndLastYear, from:<date> or span:<duration>.");
    }

    public static bool IsValid(string rule)
    {
        if (rule.Length > MaxLength) return false;
        if (rule is "none" or "all" or "thisYear" or "thisAndLastYear") return true;
        if (rule.StartsWith("from:", StringComparison.Ordinal))
            return DateOnly.TryParseExact(rule[5..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year >= 1900;
        if (!rule.StartsWith("span:", StringComparison.Ordinal)) return false;
        var match = Duration().Match(rule[5..]);
        if (!match.Success || match.Groups["time"].Value == "T") return false; // "PT" alone, or a T without a time part
        double Part(string name) => match.Groups[name].Success ? double.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;
        var days = Part("y") * 365.25 + Part("mo") * (365.25 / 12) + Part("d") + Part("h") / 24 + Part("mi") / 1440 + Part("s") / 86400;
        return days > 0 && days <= MaxDays;
    }

    [GeneratedRegex(@"^P(?:(?<y>\d{1,4})Y)?(?:(?<mo>\d{1,5})M)?(?:(?<d>\d{1,6})D)?(?<time>T(?:(?<h>\d{1,7})H)?(?:(?<mi>\d{1,9})M)?(?:(?<s>\d{1,10})S)?)?$")]
    private static partial Regex Duration();
}
