using System.Text;
using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>
/// A number found on a line, every way it can be read, where it is, how sure the OCR was, and the (folded) text right before and after
/// it, where units ("l", "Ft/l") and currencies sit.
/// </summary>
public sealed record NumberToken(string Raw, IReadOnlyList<NumberReading> Readings, int LineIndex, Box Box, double Confidence, string Before, string After);

/// <summary>Finds the numbers of a line, skipping digits that are not amounts: dates, times, percentages and long ids.</summary>
public static partial class LineScan
{
    // Thousands grouped with spaces ("24 669"), grouped with separators ("12.345,00", "24,669.00"), or plain ("38,52", "640,9").
    [GeneratedRegex(@"(?<![\p{L}\d.,])(?:\d{1,3}(?:[    ]\d{3})+(?:[.,]\d{1,3})?|\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{1,3})?|\d+(?:[.,]\d{1,3})?)(?!\d)")]
    private static partial Regex NumberPattern();

    // Percentages (VAT rates), long ids (card, receipt and tax numbers) and dashed ids ("12345678-2-42").
    [GeneratedRegex(@"\d+(?:[.,]\d+)?\s?%|\d{8,}|\d+(?:-\d+){2,}")]
    private static partial Regex NoisePattern();

    public static IReadOnlyList<NumberToken> Numbers(OcrLine line, int lineIndex, string locale, DateOnly today)
    {
        var (text, spans) = Join(OcrRepair.Repair(line));
        var masked = new StringBuilder(text);
        foreach (var date in Dates.Find(text, today)) Blank(masked, date.Index, date.Length);
        foreach (var (index, length) in Dates.FindTimes(text)) Blank(masked, index, length);
        foreach (Match noise in NoisePattern().Matches(text)) Blank(masked, noise.Index, noise.Length);

        var tokens = new List<NumberToken>();
        foreach (Match m in NumberPattern().Matches(masked.ToString()))
        {
            var readings = Text.Numbers.Interpret(m.Value, locale);
            if (readings.Count == 0) continue;
            var words = spans.Where(s => s.Start < m.Index + m.Length && m.Index < s.End).Select(s => s.Word).ToList();
            if (words.Count == 0) continue;
            var before = Folding.Fold(text[Math.Max(0, m.Index - 12)..m.Index]);
            var after = Folding.Fold(text[(m.Index + m.Length)..Math.Min(text.Length, m.Index + m.Length + 14)]);
            tokens.Add(new NumberToken(m.Value, readings, lineIndex, Box.Union(words.Select(w => w.Box)), words.Min(w => w.Confidence), before, after));
        }
        return tokens;
    }

    /// <summary>The line as one string (words joined by spaces) and where each word sits in it.</summary>
    public static (string Text, IReadOnlyList<(int Start, int End, OcrWord Word)> Spans) Join(OcrLine line)
    {
        var text = new StringBuilder();
        var spans = new List<(int, int, OcrWord)>();
        foreach (var word in line.Words)
        {
            if (text.Length > 0) text.Append(' ');
            spans.Add((text.Length, text.Length + word.Text.Length, word));
            text.Append(word.Text);
        }
        return (text.ToString(), spans);
    }

    private static void Blank(StringBuilder text, int index, int length)
    {
        for (var i = index; i < index + length && i < text.Length; i++) text[i] = ' ';
    }
}
