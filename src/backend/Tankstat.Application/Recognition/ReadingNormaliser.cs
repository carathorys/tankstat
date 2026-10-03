using System.Globalization;
using System.Text.RegularExpressions;
using Tankstat.Domain.Recognition;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Recognition;

/// <summary>
/// Turns what a provider wrote into the values a reading keeps: invariant numbers rounded like the app stores them, ISO dates,
/// upper-case currency codes and trimmed titles. A value that does not fit (not a number, not positive, an impossible date) is
/// dropped rather than kept: a blank field is better than a wrong one.
/// </summary>
public static partial class ReadingNormaliser
{
    public static IReadOnlyList<ReadingValue> Normalise(IEnumerable<RecognizedValue> values) =>
        values.Select(v => Normalise(v.Name, v.Value) is { } text ? new ReadingValue(v.Name, text, v.Confidence, v.Source) : null)
            .OfType<ReadingValue>()
            .ToList();

    public static string? Normalise(ReadingFieldName name, string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return null;
        return name switch
        {
            ReadingFieldName.Odometer => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var km)
                ? km.ToString(CultureInfo.InvariantCulture) : null,
            ReadingFieldName.Total => Amount(text, decimals: 2, max: 999_999_999_999.99m),
            ReadingFieldName.Volume => Amount(text, decimals: 3, max: 999_999.999m),
            ReadingFieldName.UnitPrice => Amount(text, decimals: 3, max: 999_999.999m),
            ReadingFieldName.Currency => CurrencyCode().IsMatch(text) ? text.ToUpperInvariant() : null,
            ReadingFieldName.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            ReadingFieldName.Title => Title(text),
            _ => null,
        };
    }

    private static string? Amount(string text, int decimals, decimal max)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        value = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return value > 0 && value <= max ? value.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture) : null;
    }

    private static string? Title(string text)
    {
        var single = Spaces().Replace(text, " ");
        return single.Length <= Expense.MaxTitleLength ? single : single[..Expense.MaxTitleLength].TrimEnd();
    }

    [GeneratedRegex("^[A-Za-z]{3}$")]
    private static partial Regex CurrencyCode();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
