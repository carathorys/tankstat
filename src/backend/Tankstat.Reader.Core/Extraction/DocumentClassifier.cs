using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>Tells receipts from everything else, and fuel receipts from other expenses, by what is printed on them.</summary>
public static partial class DocumentClassifier
{
    [GeneratedRegex(@"\d[.,]\d{2,3}\s?(?:l|ltr|liter|litre)(?![a-z])")]
    private static partial Regex Litres();

    [GeneratedRegex(@"(?<!\d)\d+[.,]\d{2}(?!\d)")]
    private static partial Regex Amount();

    /// <summary>Lines the OCR is at least this sure of (0 to 100) are text; the rest is what it makes of a photo of something else.</summary>
    private const double SureLine = 40;

    /// <summary>Whether the OCR is sure enough of the line for it to count as text (see <see cref="SureLine"/>).</summary>
    public static bool IsSure(OcrLine line) => line.Confidence >= SureLine;

    /// <summary>From this score on a page is a receipt.</summary>
    public const double ReceiptFrom = 1.5;

    /// <summary>From this score on a page may be a weak receipt, read when nothing better turns up.</summary>
    public const double WeakReceiptFrom = 0.8;

    /// <summary>The most a page without a total keyword or an amount of money can score: just short of being a receipt.</summary>
    private const double NoMoneyCap = ReceiptFrom - 0.1;

    /// <summary>
    /// How much the page looks like a receipt: a total keyword, currency markers next to numbers, several lines of text, a date, fuel
    /// words. From <see cref="ReceiptFrom"/> on it is a receipt. The OCR makes plenty of "text" and stray signs of a photo of anything else (a
    /// dashboard, say), so only lines it is sure of count, a page it doubts for the most part gets nothing for its lines of text,
    /// and without a total keyword, a currency sign beside a number or a couple of amounts of money (dates, clocks and ids are none)
    /// the page is never a receipt, however much was read.
    /// </summary>
    public static double ReceiptScore(OcrPage page, Lexicon lexicon, DateOnly today)
    {
        var lines = page.Lines.Where(IsSure).ToList();
        if (lines.Count == 0) return 0;
        var folded = lines.Select(l => Folding.Fold(l.Text)).ToList();
        var masked = folded.Select(f => LineScan.Mask(f, today)).ToList(); // a date, a clock or an id is no amount of money
        var total = folded.Select(lexicon.TotalWeight).Max();
        var currencies = folded.Zip(masked).Sum(p => Currencies.Find(p.First).Count(c => Currencies.NextToANumber(p.Second, c.Index, c.Length)));
        var amounts = masked.Count(m => Amount().IsMatch(m));
        var legible = lines.Count * 2 >= page.Lines.Count;
        var textLines = legible ? lines.Count(l => l.Text.Count(char.IsLetter) >= 3) : 0;
        var dated = lines.Any(l => Dates.Find(l.Text, today).Count > 0);
        var fuel = folded.Any(f => lexicon.HasFuelWord(f) || lexicon.HasUnitPriceWord(f));
        var score = 1.5 * total + Math.Min(1.2, 0.3 * currencies) + (textLines >= 6 ? 1.0 : textLines >= 3 ? 0.4 : 0) + (dated ? 0.5 : 0) + (fuel ? 0.5 : 0);
        return total > 0 || currencies > 0 || amounts >= 2 ? score : Math.Min(score, NoMoneyCap);
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
