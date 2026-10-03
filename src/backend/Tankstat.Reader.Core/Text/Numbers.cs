using System.Globalization;

namespace Tankstat.Reader.Core.Text;

/// <summary>One way to read a printed number: its value and how many decimals it was printed with.</summary>
public readonly record struct NumberReading(decimal Value, int Decimals);

/// <summary>
/// The value of a number as printed on a receipt in any of the supported conventions: "24 669", "12.345,00", "24,669.00",
/// "38,52", "640,9". A single separator followed by exactly three digits is ambiguous ("38,520" litres or "24.669" forints), so both
/// readings are returned, the locale's usual one first; the callers decide with the context (a unit, a currency, a sum that fits).
/// </summary>
public static class Numbers
{
    public static IReadOnlyList<NumberReading> Interpret(string raw, string locale)
    {
        var s = raw.Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace("'", "");
        var dot = s.LastIndexOf('.');
        var comma = s.LastIndexOf(',');
        if (dot < 0 && comma < 0) return Read(s, 0);

        if (dot >= 0 && comma >= 0)
        {
            var decimalAt = Math.Max(dot, comma);
            var thousands = decimalAt == dot ? "," : ".";
            return Read(s[..decimalAt].Replace(thousands, "") + "." + s[(decimalAt + 1)..], s.Length - decimalAt - 1);
        }

        var separator = dot >= 0 ? '.' : ',';
        if (s.Count(c => c == separator) > 1) return Read(s.Replace(separator.ToString(), ""), 0);

        var digitsAfter = s.Length - s.IndexOf(separator) - 1;
        var asDecimal = Read(s.Replace(separator, '.'), digitsAfter);
        if (digitsAfter != 3) return asDecimal;

        var asThousands = Read(s.Replace(separator.ToString(), ""), 0);
        // Hungarian and German group thousands with '.', English with ','; the other one separates the decimals.
        var thousandsFirst = separator == (locale == "en" ? ',' : '.');
        return thousandsFirst ? [.. asThousands, .. asDecimal] : [.. asDecimal, .. asThousands];
    }

    /// <summary>The value as an invariant string with at most <paramref name="maxDecimals"/> decimals ("38.52", "24669").</summary>
    public static string Format(decimal value, int maxDecimals) =>
        Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero)
            .ToString(maxDecimals == 0 ? "0" : "0." + new string('#', maxDecimals), CultureInfo.InvariantCulture);

    private static NumberReading[] Read(string s, int decimals) =>
        decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? [new NumberReading(v, decimals)] : [];
}
