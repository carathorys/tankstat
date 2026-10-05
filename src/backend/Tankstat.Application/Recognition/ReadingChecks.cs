using System.Globalization;
using Tankstat.Domain.Recognition;

namespace Tankstat.Application.Recognition;

/// <summary>
/// What makes a value a model read believable. A model rates its own values, but those ratings are not calibrated: what the app can check
/// decides instead (an odometer against the vehicle's latest reading, litres × price against the total, a date against today, plain
/// ranges), and the model's rating only ever lowers the result. A value that fails a check is dropped (a blank beats a wrong value); one
/// that is possible but unlikely is capped at <see cref="Doubtful"/>, below what the app fills in by default. Every drop and every doubt
/// comes with its reason (<see cref="ReadingIssue"/>), so that "nothing was filled in" can be explained.
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
    /// (never more than the model's own). A fuel receipt's litres and price are checked against its total too. What was dropped or doubted
    /// comes back with its reason; a value the kind of photo cannot show (the model listed more than it was asked for) is only left out.
    /// </summary>
    public static CheckedValues Check(DocumentKind kind, IEnumerable<RecognizedValue> values, RecognitionRequest hints)
    {
        var allowed = FieldsOf(kind);
        var kept = new List<RecognizedValue>();
        var issues = new List<ReadingIssue>();
        foreach (var value in values)
        {
            if (!allowed.Contains(value.Name)) continue;
            if (ReadingNormaliser.Normalise(value.Name, value.Value) is not { } text)
            {
                issues.Add(new ReadingIssue(value.Name, ReadingIssueCode.NotUnderstood));
                continue;
            }
            var verdict = Believable(value.Name, text, hints);
            if (verdict.Issue is { } code) issues.Add(new ReadingIssue(value.Name, code));
            if (verdict.Cap is not { } cap) continue;
            var rated = Rated(value.Confidence);
            if (rated is null) issues.Add(new ReadingIssue(value.Name, ReadingIssueCode.NoConfidence));
            kept.Add(value with { Value = text, Confidence = Math.Min(rated ?? Doubtful, cap) });
        }
        return new CheckedValues(kind == DocumentKind.FuelReceipt ? CrossChecked(kept, issues, hints) : kept, issues);
    }

    /// <summary>The model's own rating on the scale of 0 to 1: one given in per cent is scaled down; null when it is no rating at all (none given, not a number, on no scale), which counts as doubtful.</summary>
    private static double? Rated(double confidence)
    {
        if (double.IsNaN(confidence)) return null;
        return confidence switch
        {
            > 1 and <= 100 => confidence / 100,
            > 100 or < 0 => null,
            _ => confidence,
        };
    }

    /// <summary>What a check says of one value: how far it can be believed on its own (null: not at all, it is dropped) and, when that is not simply "fully", why.</summary>
    private readonly record struct Verdict(double? Cap, ReadingIssueCode? Issue = null)
    {
        public static readonly Verdict Fine = new(1);

        public static Verdict Drop(ReadingIssueCode issue) => new(null, issue);

        public static Verdict Doubt(ReadingIssueCode issue) => new(Doubtful, issue);
    }

    private static Verdict Believable(ReadingFieldName name, string text, RecognitionRequest hints) => name switch
    {
        ReadingFieldName.Odometer => Odometer(long.Parse(text, CultureInfo.InvariantCulture), hints.LastOdometer),
        ReadingFieldName.Date => Date(DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture), hints.Today),
        ReadingFieldName.Volume => Amount(text) is >= 0.3m and <= 400m ? Verdict.Fine : Verdict.Drop(ReadingIssueCode.OutOfRange), // a moped's top-up to a lorry's tank
        ReadingFieldName.Total => Amount(text) < 10_000_000m ? Verdict.Fine : Verdict.Drop(ReadingIssueCode.OutOfRange),
        ReadingFieldName.UnitPrice => Amount(text) < 100_000m ? Verdict.Fine : Verdict.Drop(ReadingIssueCode.OutOfRange),
        _ => Verdict.Fine,
    };

    /// <summary>An odometer never goes back, and between two logs it does not advance a hundred thousand; without a latest reading there is nothing to check against.</summary>
    private static Verdict Odometer(long value, long? last)
    {
        if (last is not { } latest) return Verdict.Fine;
        if (value < latest) return Verdict.Drop(ReadingIssueCode.OdometerBelowLatest);
        if (value - latest > OdometerRange) return Verdict.Drop(ReadingIssueCode.OdometerTooFarAbove);
        return value - latest > OdometerNear ? Verdict.Doubt(ReadingIssueCode.OdometerFarAbove) : Verdict.Fine;
    }

    private static Verdict Date(DateOnly date, DateOnly today) =>
        date > today.AddDays(1) ? Verdict.Drop(ReadingIssueCode.DateInFuture)
        : date < today.AddYears(-DateYearsBack) ? Verdict.Drop(ReadingIssueCode.DateTooOld)
        : Verdict.Fine;

    /// <summary>All three amounts read but not fitting: one of them is misread, most likely the litres or the price, so those two are doubted and the total stays.</summary>
    private static List<RecognizedValue> CrossChecked(List<RecognizedValue> kept, List<ReadingIssue> issues, RecognitionRequest hints)
    {
        RecognizedValue? Of(ReadingFieldName name) => kept.FirstOrDefault(v => v.Name == name);
        if (Of(ReadingFieldName.Total) is not { } total || Of(ReadingFieldName.Volume) is not { } volume || Of(ReadingFieldName.UnitPrice) is not { } price) return kept;
        var currency = Of(ReadingFieldName.Currency)?.Value ?? hints.Currency;
        if (Fits(Amount(total.Value), Amount(volume.Value) * Amount(price.Value), currency)) return kept;
        issues.Add(new ReadingIssue(ReadingFieldName.Volume, ReadingIssueCode.AmountsDoNotAdd));
        issues.Add(new ReadingIssue(ReadingFieldName.UnitPrice, ReadingIssueCode.AmountsDoNotAdd));
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

/// <summary>What the checks made of a model's values: those worth keeping (normalised, with the confidence allowed) and why the others were dropped or doubted.</summary>
public sealed record CheckedValues(IReadOnlyList<RecognizedValue> Kept, IReadOnlyList<ReadingIssue> Issues);
