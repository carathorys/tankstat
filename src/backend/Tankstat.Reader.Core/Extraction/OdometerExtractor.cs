using System.Globalization;
using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>
/// Reads the odometer from a dashboard photo: the most prominent group of 4 to 7 digits that is not something else a dashboard shows
/// (the trip meter has a decimal, the clock a colon, the temperature a degree sign). Digit passes (which cannot see those signs)
/// are checked against the text passes. The vehicle's latest known reading is the strongest hint: an odometer never goes back.
/// </summary>
public sealed partial class OdometerExtractor(Lexicon lexicon)
{
    [GeneratedRegex(@"\d+[.,]\d+|(?<!\d)\d{1,2}:\d{2}(?!\d)|-?\d+\s?°")]
    private static partial Regex NotAnOdometer();

    [GeneratedRegex(@"^(\d+)(?:km|mi)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DigitsWord();

    private sealed record Group(long Value, int Digits, Box Box, double Confidence, bool Km, bool Trip, int MedianHeight);

    public ReadField? Extract(IReadOnlyList<OcrPage> textPages, IReadOnlyList<OcrPage> digitPages, ReadHints hints)
    {
        var excluded = new HashSet<string>();
        var kmOnPage = false;
        foreach (var line in textPages.SelectMany(p => p.Lines))
        {
            foreach (Match m in NotAnOdometer().Matches(line.Text)) excluded.Add(new string(m.Value.Where(char.IsDigit).ToArray()));
            kmOnPage |= lexicon.HasOdometerWord(Folding.Fold(line.Text));
        }

        var groups = textPages.Concat(digitPages)
            .SelectMany(page => page.Lines.SelectMany(line => Groups(page, line)))
            .Where(g => g.Digits is >= 1 and <= 7 && !excluded.Contains(g.Value.ToString(CultureInfo.InvariantCulture)))
            .Where(g => Plausible(g.Value, hints.LastOdometer))
            .ToList();
        if (groups.Count == 0) return null;

        var best = groups
            .GroupBy(g => g.Value)
            .Select(same => (Group: same.MaxBy(g => Score(g, kmOnPage, hints))!, Support: same.Count()))
            .Select(x => (x.Group, Score: Score(x.Group, kmOnPage, hints) + 0.4 * (x.Support - 1)))
            .MaxBy(x => x.Score);
        return best.Score < 0.8
            ? null
            : new ReadField(FieldNames.Odometer, best.Group.Value.ToString(CultureInfo.InvariantCulture), Confidence.From(best.Score, best.Group.Confidence, 1.5), FieldSources.Ocr);
    }

    /// <summary>
    /// With the latest known reading, an odometer below it (it never goes back) or more than 100 000 above it (between two logs) is a
    /// misread: no reading is better than a confident wrong one.
    /// </summary>
    private static bool Plausible(long value, long? last) => last is not { } l || (value >= l && value - l <= 100_000);

    private static double Score(Group g, bool kmOnPage, ReadHints hints)
    {
        var score = g.Digits switch { <= 1 => -2, 2 => -1.5, 3 => -0.8, 4 => 0.2, 5 => 0.8, 6 => 1.0, _ => 0.6 };
        var scale = g.MedianHeight > 0 ? (double)g.Box.Height / g.MedianHeight : 1;
        score += 0.8 * Math.Clamp(scale - 1, 0, 1.5);
        score += g.Km ? 1.0 : kmOnPage ? 0.3 : 0;
        score -= g.Trip ? 1.5 : 0;
        score -= g.Digits == 4 && g.Value / 100 <= 23 && g.Value % 100 <= 59 ? 1.2 : 0; // a clock whose colon the OCR lost ("02:06" → "0206")
        if (hints.LastOdometer is { } last) score += g.Value - last <= 5000 ? 2.5 : g.Value - last <= 50_000 ? 0.5 : -0.5;
        return score;
    }

    /// <summary>Runs of digit words on a line, joining neighbours a small gap apart ("123 456" on some clusters), with the line's hints.</summary>
    private IEnumerable<Group> Groups(OcrPage page, OcrLine line)
    {
        var folded = Folding.Fold(line.Text);
        var km = lexicon.HasOdometerWord(folded);
        var trip = lexicon.HasTripWord(folded);
        var run = new List<(OcrWord Word, string Digits)>();

        IEnumerable<Group> Flush()
        {
            if (run.Count == 0) yield break;
            var digits = string.Concat(run.Select(r => r.Digits));
            if (digits.Length <= 18 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                yield return new Group(value, digits.Length, Box.Union(run.Select(r => r.Word.Box)), run.Min(r => r.Word.Confidence), km, trip, page.MedianWordHeight);
            run.Clear();
        }

        foreach (var word in line.Words)
        {
            var m = DigitsWord().Match(word.Text);
            if (!m.Success)
            {
                foreach (var g in Flush()) yield return g;
                continue;
            }
            if (run.Count > 0 && word.Box.Left - run[^1].Word.Box.Right > Math.Max(word.Box.Height, run[^1].Word.Box.Height))
                foreach (var g in Flush()) yield return g;
            run.Add((word, m.Groups[1].Value));
            if (m.Groups[1].Length < word.Text.Length) // "123456km": the unit ends the run
                foreach (var g in Flush()) yield return g;
        }
        foreach (var g in Flush()) yield return g;
    }
}
