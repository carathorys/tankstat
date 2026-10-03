using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>Tells receipts from everything else, and fuel receipts from other expenses, by what is printed on them.</summary>
public static partial class DocumentClassifier
{
    [GeneratedRegex(@"\d[.,]\d{2,3}\s?(?:l|ltr|liter|litre)(?![a-z])")]
    private static partial Regex Litres();

    /// <summary>
    /// How much the page looks like a receipt: a total keyword, currency markers, several lines of text, a date, fuel words.
    /// From about 1.5 on it is a receipt.
    /// </summary>
    public static double ReceiptScore(OcrPage page, Lexicon lexicon, DateOnly today)
    {
        if (page.Lines.Count == 0) return 0;
        var folded = page.Lines.Select(l => Folding.Fold(l.Text)).ToList();
        var total = folded.Select(lexicon.TotalWeight).Max();
        var currencies = folded.Sum(f => Currencies.Find(f).Count());
        var textLines = page.Lines.Count(l => l.Text.Count(char.IsLetter) >= 3);
        var dated = page.Lines.Any(l => Dates.Find(l.Text, today).Count > 0);
        var fuel = folded.Any(f => lexicon.HasFuelWord(f) || lexicon.HasUnitPriceWord(f));
        return 1.5 * total + Math.Min(1.2, 0.3 * currencies) + (textLines >= 6 ? 1.0 : textLines >= 3 ? 0.4 : 0) + (dated ? 0.5 : 0) + (fuel ? 0.5 : 0);
    }

    /// <summary>Which receipt kind the page is, of those the caller accepts.</summary>
    public static string ReceiptKind(OcrPage page, IReadOnlySet<string> allowed, Lexicon lexicon)
    {
        var fuelAllowed = allowed.Contains(DocumentKinds.FuelReceipt);
        var expenseAllowed = allowed.Contains(DocumentKinds.ExpenseReceipt);
        if (fuelAllowed != expenseAllowed) return fuelAllowed ? DocumentKinds.FuelReceipt : DocumentKinds.ExpenseReceipt;
        var looksLikeFuel = page.Lines.Select(l => Folding.Fold(l.Text)).Any(f => lexicon.HasFuelWord(f) || lexicon.HasUnitPriceWord(f) || Litres().IsMatch(f));
        return looksLikeFuel ? DocumentKinds.FuelReceipt : DocumentKinds.ExpenseReceipt;
    }
}
