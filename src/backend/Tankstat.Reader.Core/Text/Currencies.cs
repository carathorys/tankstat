using System.Text.RegularExpressions;

namespace Tankstat.Reader.Core.Text;

/// <summary>Currency markers on receipts, as ISO codes: the codes themselves, symbols and the local abbreviations ("Ft", "zł", "Kč").</summary>
public static partial class Currencies
{
    private static readonly Dictionary<string, string> Markers = new(StringComparer.Ordinal)
    {
        ["huf"] = "HUF", ["ft"] = "HUF",
        ["eur"] = "EUR", ["€"] = "EUR",
        ["usd"] = "USD", ["$"] = "USD",
        ["gbp"] = "GBP", ["£"] = "GBP",
        ["chf"] = "CHF",
        ["pln"] = "PLN", ["zl"] = "PLN",
        ["czk"] = "CZK", ["kc"] = "CZK",
        ["ron"] = "RON", ["lei"] = "RON",
        ["bgn"] = "BGN", ["rsd"] = "RSD",
        ["sek"] = "SEK", ["nok"] = "NOK",
        ["dkk"] = "DKK", ["kr"] = "DKK",
    };

    // On folded text (lower case, no accents): a marker is a word of its own or glued to a number ("24669ft", "€12.30").
    [GeneratedRegex(@"(?<![a-z])(huf|ft|eur|usd|gbp|chf|pln|zl|czk|kc|ron|lei|bgn|rsd|sek|nok|dkk)(?![a-z])|[€$£]")]
    private static partial Regex MarkerPattern();

    /// <summary>Every currency marker in a folded text, with where it starts.</summary>
    public static IEnumerable<(string Code, int Index, int Length)> Find(string folded) =>
        MarkerPattern().Matches(folded).Select(m => (Markers[m.Value], m.Index, m.Length));

    /// <summary>
    /// Whether a marker found by <see cref="Find"/> stands next to a number ("24 687 Ft", "€12.30"), as on a receipt; one on its own
    /// may be a letter pair or a stray sign the OCR made of a photo of something else. Pass the text with the digits that are no
    /// amounts blanked out (<c>LineScan.Mask</c>): a time of day, a date or an id beside a sign is no price.
    /// </summary>
    public static bool NextToANumber(string folded, int index, int length)
    {
        var before = index - 1;
        while (before >= 0 && BeforeMarker(folded[before])) before--;
        var after = index + length;
        while (after < folded.Length && AfterMarker(folded[after])) after++;
        return (before >= 0 && char.IsDigit(folded[before])) || (after < folded.Length && char.IsDigit(folded[after]));
    }

    /// <summary>What may stand between an amount and the marker after it: spaces and the dashes and brackets of "24 687,- Ft", "56.20/USD", "69,30 (EUR)".</summary>
    private static bool BeforeMarker(char c) => c is ' ' or '-' or '–' or ',' or '.' or '(' or '/' or ';';

    /// <summary>What may stand between a marker and the amount after it: spaces and the colon or bracket of "Ft: 24 687", "(EUR) 69,30".</summary>
    private static bool AfterMarker(char c) => c is ' ' or ':' or ')' or '-' or '–';

    /// <summary>The minor units a currency is usually printed with (HUF receipts show whole forints).</summary>
    public static int Decimals(string code) => code is "HUF" ? 0 : 2;
}
