using System.Globalization;
using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>
/// Reads the odometer from a dashboard photo: the most prominent group of 3 to 7 digits that is not something else a dashboard shows
/// (the trip meter has a decimal, the clock a colon, the temperature a degree sign, a service countdown says "in … km", gauge labels
/// are short round numbers). Digit passes (which cannot see those signs) are checked against the text passes. The vehicle's latest
/// known reading is the strongest hint: an odometer never goes back.
/// </summary>
public sealed partial class OdometerExtractor(Lexicon lexicon)
{
    [GeneratedRegex(@"\d+[.,]\d+|(?<!\d)\d{1,2}:\d{2}(?!\d)|-?\d+\s?°")]
    private static partial Regex NotAnOdometer();

    [GeneratedRegex(@"^(\d+)(?:km|mi)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DigitsWord();

    /// <summary>How many further passes seeing the same value count: gauge labels and stray digits turn up in every pass.</summary>
    private const int SupportLimit = 3;

    private sealed record Group(OcrPage Page, long Value, int Digits, Box Box, double Confidence, bool Km, bool NotOdometer, int MedianHeight);

    public ReadField? Extract(IReadOnlyList<OcrPage> textPages, IReadOnlyList<OcrPage> digitPages, ReadHints hints)
    {
        var excluded = new HashSet<string>();
        var kmOnPage = false;
        foreach (var line in textPages.SelectMany(p => p.Lines))
        {
            foreach (Match m in NotAnOdometer().Matches(line.Text)) excluded.Add(new string(m.Value.Where(char.IsDigit).ToArray()));
            kmOnPage |= lexicon.HasOdometerWord(Folding.Fold(line.Text));
        }

        var all = textPages.Concat(digitPages).SelectMany(page => page.Lines.SelectMany(line => Groups(page, line))).ToList();
        // The labels of the gauges: short round numbers (20, 40 … 100, 120). Two side by side are often read as one ("100120").
        var labels = all.Where(g => g.Digits is 2 or 3 && g.Value > 0 && g.Value % 10 == 0).Select(g => g.Value).ToHashSet();
        var groups = all
            .Where(g => g.Digits is >= 3 and <= 7 && !excluded.Contains(g.Value.ToString(CultureInfo.InvariantCulture)))
            .Where(g => g.Digits == 3 || labels.Count < 3 || !GluedLabels(g.Value.ToString(CultureInfo.InvariantCulture), labels))
            .Where(g => Plausible(g.Value, hints.LastOdometer))
            .ToList();
        if (groups.Count == 0) return null;

        // Other passes seeing the same value confirm it, up to a point: the labels of a gauge are seen by every pass too. A pass that
        // lost the first or last digit (a dark edge of the display) confirms the one that read them all with more certainty.
        var values = groups
            .GroupBy(g => g.Value)
            .Select(same => new Candidate(same.Key.ToString(CultureInfo.InvariantCulture), same.MaxBy(g => Score(g, kmOnPage, hints))!,
                same.Select(g => g.Page).ToHashSet(), same.Max(g => g.Confidence)))
            .ToList();
        var ranked = values
            .Where(v => !values.Any(whole => v.IsPartOf(whole)))
            .Select(v => (v.Best, Pages: v.Pages.Union(values.Where(part => part.IsPartOf(v)).SelectMany(part => part.Pages)).Count()))
            .Select(x => (Group: x.Best, Score: Score(x.Best, kmOnPage, hints) + 0.4 * Math.Min(SupportLimit, x.Pages - 1)))
            .OrderByDescending(x => x.Score)
            .ToList();
        var best = ranked[0];
        if (best.Score < 0.8) return null;
        // Another reading nearly as likely (a misread of the same digits by another pass, say) leaves the reader unsure, whichever it picks.
        var margin = ranked.Count > 1 ? best.Score - ranked[1].Score : double.MaxValue;
        var certainty = best.Score - Math.Max(0, 0.5 - margin);
        return new ReadField(FieldNames.Odometer, best.Group.Value.ToString(CultureInfo.InvariantCulture), Confidence.From(certainty, best.Group.Confidence, 1.5), FieldSources.Ocr);
    }

    /// <summary>One value with the passes that saw it; <see cref="Confidence"/> is the OCR's best certainty of it.</summary>
    private sealed record Candidate(string Text, Group Best, HashSet<OcrPage> Pages, double Confidence)
    {
        /// <summary>Whether this is the other value with its first or last digits lost, read with no more certainty than the whole.</summary>
        public bool IsPartOf(Candidate whole) =>
            Text.Length >= 4 && whole.Text.Length > Text.Length && whole.Confidence >= Confidence
            && (whole.Text.StartsWith(Text, StringComparison.Ordinal) || whole.Text.EndsWith(Text, StringComparison.Ordinal));
    }

    /// <summary>Whether the digits are gauge labels run together, each 2 or 3 digits long ("100120" where 100 and 120 are labels).</summary>
    private static bool GluedLabels(string digits, HashSet<long> labels)
    {
        if (digits.Length == 0) return true;
        foreach (var take in new[] { 2, 3 })
            if (digits.Length >= take && digits.Length - take != 1 && labels.Contains(long.Parse(digits[..take], CultureInfo.InvariantCulture)) && GluedLabels(digits[take..], labels))
                return true;
        return false;
    }

    /// <summary>
    /// With the latest known reading, an odometer below it (it never goes back) or more than 100 000 above it (between two logs) is a
    /// misread: no reading is better than a confident wrong one.
    /// </summary>
    private static bool Plausible(long value, long? last) => last is not { } l || (value >= l && value - l <= 100_000);

    private static double Score(Group g, bool kmOnPage, ReadHints hints)
    {
        var score = g.Digits switch { 3 => -0.8, 4 => 0.2, 5 => 0.8, 6 => 1.0, _ => 0.6 };
        var scale = g.MedianHeight > 0 ? (double)g.Box.Height / g.MedianHeight : 1;
        score += 0.6 * Math.Clamp(scale - 1, 0, 1); // bigger than the rest helps, but the labels of a speedometer are big too
        score += 0.8 * (Math.Clamp(g.Confidence, 0, 100) / 100 - 0.5); // what the OCR itself doubts counts less
        score += g.Km ? 1.0 : kmOnPage ? 0.3 : 0;
        score -= g.NotOdometer ? 1.5 : 0;
        score -= g.Digits == 4 && g.Value / 100 <= 23 && g.Value % 100 <= 59 ? 1.2 : 0; // a clock whose colon the OCR lost ("02:06" → "0206")
        score -= g.Value % 100 == 0 ? 0.6 : 0; // round numbers are gauge labels and service intervals far more often than readings
        if (hints.LastOdometer is { } last) score += g.Value - last <= 5000 ? 2.5 : g.Value - last <= 50_000 ? 0.5 : -0.5;
        return score;
    }

    /// <summary>
    /// Runs of digit words on a line, joining neighbours a small gap apart ("123 456" on some clusters), with the line's hints. The
    /// digits of one number are all one size: a gauge label next to the odometer is never part of it.
    /// </summary>
    private IEnumerable<Group> Groups(OcrPage page, OcrLine line)
    {
        var folded = Folding.Fold(line.Text);
        var km = lexicon.HasOdometerWord(folded);
        var notOdometer = lexicon.HasTripWord(folded) || lexicon.HasCountdownWord(folded);
        var run = new List<(OcrWord Word, string Digits)>();

        IEnumerable<Group> Flush()
        {
            if (run.Count == 0) yield break;
            var digits = string.Concat(run.Select(r => r.Digits));
            if (digits.Length <= 18 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                yield return new Group(page, value, digits.Length, Box.Union(run.Select(r => r.Word.Box)), run.Min(r => r.Word.Confidence), km, notOdometer, page.MedianWordHeight);
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
            if (run.Count > 0 && !Continues(run[^1].Word.Box, word.Box))
                foreach (var g in Flush()) yield return g;
            run.Add((word, m.Groups[1].Value));
            if (m.Groups[1].Length < word.Text.Length) // "123456km": the unit ends the run
                foreach (var g in Flush()) yield return g;
        }
        foreach (var g in Flush()) yield return g;
    }

    private static bool Continues(Box previous, Box next)
    {
        var height = Math.Max(previous.Height, next.Height);
        return next.Left - previous.Right <= height && Math.Abs(next.Height - previous.Height) <= 0.35 * height;
    }
}
