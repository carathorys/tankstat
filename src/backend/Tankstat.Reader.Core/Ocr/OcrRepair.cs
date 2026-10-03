using System.Text.RegularExpressions;

namespace Tankstat.Reader.Core.Ocr;

/// <summary>
/// Undoes the OCR's typical slips on receipts, word by word so every word keeps its place: a space after or before a decimal
/// separator ("11, 47" → "11,47", "£44 .53" → "£44.53"), the litre sign read as a one or a bar ("38,52 1 x 640,9" → "38,52 l x 640,9";
/// "Ft/1", "EUR/1l" → "Ft/l").
/// </summary>
public static partial class OcrRepair
{
    [GeneratedRegex(@"^\d+[.,]$")]
    private static partial Regex DanglingDecimal();

    [GeneratedRegex(@"^\d{1,3}$")]
    private static partial Regex ShortDigits();

    [GeneratedRegex(@"\d$")]
    private static partial Regex EndsWithDigit();

    [GeneratedRegex(@"^[.,]\d{1,3}$")]
    private static partial Regex DetachedDecimals();

    [GeneratedRegex(@"^\d+[.,]\d{1,3}$")]
    private static partial Regex Decimal();

    [GeneratedRegex(@"^(.*/)(?:[1Il|i]{1,2}|L)$")]
    private static partial Regex PerLitre();

    // "l" read as "1", "I", "|", "i", "1L", "|1" ... (and the genuine "l" or "L", which need no repair but count for the rule).
    [GeneratedRegex(@"^[1Il|iL]{1,2}$")]
    private static partial Regex LitreLookalike();

    private static readonly string[] Multiply = ["x", "X", "×", "*", "@"];

    public static OcrLine Repair(OcrLine line)
    {
        var words = new List<OcrWord>(line.Words.Count);
        for (var i = 0; i < line.Words.Count; i++)
        {
            var word = line.Words[i];
            var joinsDecimals = EndsWithDigit().IsMatch(word.Text) && i + 1 < line.Words.Count && DetachedDecimals().IsMatch(line.Words[i + 1].Text);
            if (joinsDecimals || (DanglingDecimal().IsMatch(word.Text) && i + 1 < line.Words.Count && ShortDigits().IsMatch(line.Words[i + 1].Text)))
            {
                var next = line.Words[++i];
                words.Add(new OcrWord(word.Text + next.Text, Math.Min(word.Confidence, next.Confidence), Box.Union([word.Box, next.Box])));
                continue;
            }
            if (LitreLookalike().IsMatch(word.Text) && words.Count > 0 && Decimal().IsMatch(words[^1].Text)
                && i + 1 < line.Words.Count && Multiply.Contains(line.Words[i + 1].Text))
            {
                words.Add(word with { Text = "l" });
                continue;
            }
            var perLitre = PerLitre().Match(word.Text);
            words.Add(perLitre.Success ? word with { Text = perLitre.Groups[1].Value + "l" } : word);
        }
        return words.Count == line.Words.Count && words.Zip(line.Words).All(p => ReferenceEquals(p.First, p.Second)) ? line : new OcrLine(words);
    }
}
