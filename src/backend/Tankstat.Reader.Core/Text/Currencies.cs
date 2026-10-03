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

    /// <summary>The minor units a currency is usually printed with (HUF receipts show whole forints).</summary>
    public static int Decimals(string code) => code is "HUF" ? 0 : 2;
}
