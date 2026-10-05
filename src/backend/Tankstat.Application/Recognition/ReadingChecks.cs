using System.Globalization;
using Tankstat.Domain.Recognition;

namespace Tankstat.Application.Recognition;

/// <summary>
/// What makes a value a model read believable. A model rates its own values, but those ratings are not calibrated: what the app can check
/// decides instead (an odometer against the vehicle's latest reading, litres × price against the total, a date against today, plain
/// ranges), and the model's rating only ever lowers the result. A value that fails a check is dropped (a blank beats a wrong value); one
/// that is possible but unlikely is capped at <see cref="Doubtful"/>, below what the app fills in by default.
/// </summary>
public static class ReadingChecks
{
    /// <summary>The confidence of a value that is possible but not to be believed on its own: below the default <see cref="RecognitionOptions.MinConfidence"/>.</summary>
    public const double Doubtful = 0.5;

    /// <summary>Up to this far above the latest reading an odometer is simply the next one; beyond it, it is doubted.</summary>
    private const long OdometerNear = 50_000;

    /// <summary>More than this above the latest reading is not the same car between two logs: a misread.</summary>
    private const long OdometerRange = 100_000;

    /// <summary>Receipts are dated between this many years back and tomorrow (tomorrow covers every time zone).</summary>
    private const int DateYearsBack = 3;

    /// <summary>The values a photo of that kind can provide (none for <see cref="DocumentKind.Unknown"/>).</summary>
    public static IReadOnlyList<ReadingFieldName> FieldsOf(DocumentKind kind) => kind switch
    {
        DocumentKind.Odometer => [ReadingFieldName.Odometer],
        DocumentKind.FuelReceipt => [ReadingFieldName.Total, ReadingFieldName.Volume, ReadingFieldName.UnitPrice, ReadingFieldName.Currency, ReadingFieldName.Date],
        DocumentKind.ExpenseReceipt => [ReadingFieldName.Total, ReadingFieldName.Currency, ReadingFieldName.Date, ReadingFieldName.Title],
        _ => [],
    };

    /// <summary>
    /// The values worth keeping of what a model read: those the kind of photo can show, normalised, with the confidence the checks allow
    /// (never more than the model's own). A fuel receipt's litres and price are checked against its total too.
    /// </summary>
    public static IReadOnlyList<RecognizedValue> Apply(DocumentKind kind, IEnumerable<RecognizedValue> values, RecognitionRequest hints)
    {
        var allowed = FieldsOf(kind);
        var kept = new List<RecognizedValue>();
        foreach (var value in values)
        {
            if (!allowed.Contains(value.Name) || ReadingNormaliser.Normalise(value.Name, value.Value) is not { } text) continue;
            if (Believable(value.Name, text, hints) is not { } cap) continue;
            kept.Add(value with { Value = text, Confidence = Math.Min(Rated(value.Confidence), cap) });
        }
        return kind == DocumentKind.FuelReceipt ? CrossChecked(kept, hints) : kept;
    }

    /// <summary>The model's own rating on the scale of 0 to 1: one given in per cent is scaled down, one that is no rating at all counts as doubtful.</summary>
    private static double Rated(double confidence)
    {
        if (double.IsNaN(confidence)) return Doubtful;
        return confidence switch
        {
            > 1 and <= 100 => confidence / 100,
            > 100 or < 0 => Doubtful,
            _ => confidence,
        };
    }

    /// <summary>How far a normalised value can be believed on its own: 1 when nothing speaks against it, <see cref="Doubtful"/> when it is unlikely, null when it cannot be right.</summary>
    private static double? Believable(ReadingFieldName name, string text, RecognitionRequest hints) => name switch
    {
        ReadingFieldName.Odometer => Odometer(long.Parse(text, CultureInfo.InvariantCulture), hints.LastOdometer),
        ReadingFieldName.Date => Date(DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture), hints.Today),
        ReadingFieldName.Volume => Amount(text) is >= 0.3m and <= 400m ? 1 : null, // a moped's top-up to a lorry's tank
        ReadingFieldName.Total => Amount(text) < 10_000_000m ? 1 : null,
        ReadingFieldName.UnitPrice => Amount(text) < 100_000m ? 1 : null,
        _ => 1,
    };

    /// <summary>An odometer never goes back, and between two logs it does not advance a hundred thousand; without a latest reading there is nothing to check against.</summary>
    private static double? Odometer(long value, long? last)
    {
        if (last is not { } latest) return 1;
        if (value < latest || value - latest > OdometerRange) return null;
        return value - latest > OdometerNear ? Doubtful : 1;
    }

    private static double? Date(DateOnly date, DateOnly today) => date > today.AddDays(1) || date < today.AddYears(-DateYearsBack) ? null : 1;

    /// <summary>All three amounts read but not fitting: one of them is misread, most likely the litres or the price, so those two are doubted and the total stays.</summary>
    private static List<RecognizedValue> CrossChecked(List<RecognizedValue> kept, RecognitionRequest hints)
    {
        RecognizedValue? Of(ReadingFieldName name) => kept.FirstOrDefault(v => v.Name == name);
        if (Of(ReadingFieldName.Total) is not { } total || Of(ReadingFieldName.Volume) is not { } volume || Of(ReadingFieldName.UnitPrice) is not { } price) return kept;
        var currency = Of(ReadingFieldName.Currency)?.Value ?? hints.Currency;
        if (Fits(Amount(total.Value), Amount(volume.Value) * Amount(price.Value), currency)) return kept;
        return kept.Select(v => v.Name is ReadingFieldName.Volume or ReadingFieldName.UnitPrice ? v with { Confidence = Math.Min(v.Confidence, Doubtful) } : v).ToList();
    }

    /// <summary>Whether a total and litres × price per litre agree: within 1.5 %, or one and a half of the currency's smallest unit.</summary>
    private static bool Fits(decimal total, decimal product, string? currency)
    {
        var unit = currency?.ToUpperInvariant() is "HUF" or "JPY" or "KRW" or "ISK" ? 1m : 0.01m; // whole units, or cents
        return Math.Abs(total - product) <= Math.Max(total * 0.015m, unit * 1.5m);
    }

    private static decimal Amount(string normalised) => decimal.Parse(normalised, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
