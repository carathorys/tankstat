using System.Text.RegularExpressions;

namespace Tankstat.Domain.Measurements;

/// <summary>An ISO 4217 currency code (EUR, USD, HUF, ...). Money amounts are stored in the currency they were paid in.</summary>
public static partial class CurrencyCode
{
    /// <summary>Upper-cases and validates the code; throws a translatable error when it is not three letters.</summary>
    public static string Normalize(string? code)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        return Pattern().IsMatch(normalized)
            ? normalized
            : throw new DomainException("money.currencyInvalid", "The currency must be a three-letter code such as EUR or USD.");
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex Pattern();
}
