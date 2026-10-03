namespace Tankstat.Reader.Core.Extraction;

/// <summary>
/// What is known about one number as a candidate for one field. The rules turn it into a score now; a trained model (later) replaces
/// <see cref="RuleScorer"/> behind <see cref="ICandidateScorer"/> and learns the weights from confirmed readings.
/// </summary>
/// <param name="TotalKeyword">Weight of the strongest total keyword on the number's line (ÖSSZESEN, TOTAL, SUMME …), 0 to 1.</param>
/// <param name="TotalKeywordAbove">The same for the line above, when that line is only a label.</param>
/// <param name="NotTotalKeyword">Weight of a "not the total" keyword on the line (subtotal, VAT, change …).</param>
/// <param name="FontScale">Height of the number relative to the page's typical word.</param>
/// <param name="VerticalPosition">0 at the top of the page, 1 at the bottom.</param>
/// <param name="Repeats">How many other numbers on the page have the same value (the total often appears twice).</param>
public sealed record CandidateFeatures(
    double TotalKeyword,
    double TotalKeywordAbove,
    double NotTotalKeyword,
    bool CurrencyAdjacent,
    bool VolumeUnit,
    bool PerUnit,
    bool AfterMultiply,
    bool UnitPriceWord,
    bool FuelWord,
    double FontScale,
    bool RightAligned,
    double VerticalPosition,
    int Repeats,
    bool IsLargest,
    int Decimals,
    bool Plausible);

public interface ICandidateScorer
{
    /// <summary>How likely the candidate is the value of <paramref name="field"/>: higher is more likely, around 1 is a weak yes.</summary>
    double Score(string field, CandidateFeatures f);
}

/// <summary>Hand-set weights: what a person looks at to find the total, the litres and the price per litre on a receipt.</summary>
public sealed class RuleScorer : ICandidateScorer
{
    public double Score(string field, CandidateFeatures f) => field switch
    {
        FieldNames.Total =>
            2.5 * f.TotalKeyword + 1.0 * f.TotalKeywordAbove - 3.0 * f.NotTotalKeyword
            + (f.CurrencyAdjacent ? 0.8 : 0) + (f.IsLargest ? 0.7 : 0) + (f.Repeats > 0 ? 0.5 : 0)
            + 0.6 * Math.Clamp(f.FontScale - 1, 0, 1) + (f.RightAligned ? 0.2 : 0) + (f.VerticalPosition > 0.35 ? 0.2 : 0)
            - (f.VolumeUnit ? 3 : 0) - (f.PerUnit ? 3 : 0) - (f.AfterMultiply ? 1.2 : 0) - (f.Plausible ? 0 : 5),
        FieldNames.Volume =>
            (f.VolumeUnit ? 3 : 0) + (f.Decimals is 2 or 3 ? 0.5 : -0.5) + (f.FuelWord ? 0.5 : 0)
            - (f.PerUnit ? 3 : 0) - (f.CurrencyAdjacent ? 1 : 0) - 1.5 * f.TotalKeyword + (f.Plausible ? 0.3 : -5),
        FieldNames.UnitPrice =>
            (f.PerUnit ? 3 : 0) + (f.AfterMultiply ? 1 : 0) + (f.UnitPriceWord ? 0.8 : 0) + (f.FuelWord ? 0.3 : 0)
            - (f.VolumeUnit ? 3 : 0) - 1.5 * f.TotalKeyword + (f.Plausible ? 0 : -5),
        _ => 0,
    };
}

internal static class Confidence
{
    /// <summary>Turns a score and the OCR's own certainty (0 to 100) into a confidence from 0 to 1.</summary>
    public static double From(double score, double ocrConfidence, double offset = 1.0)
    {
        var rule = 1 / (1 + Math.Exp(-(score - offset)));
        var ocr = 0.6 + 0.4 * Math.Clamp(ocrConfidence, 0, 100) / 100;
        return Math.Round(Math.Clamp(rule * ocr, 0, 1), 2);
    }
}
