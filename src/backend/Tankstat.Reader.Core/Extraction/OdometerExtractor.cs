using System.Globalization;
using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>
/// Reads the odometer from a dashboard photo: the most prominent group of 3 to 7 digits that is not something else a dashboard shows
/// (the trip meter has a decimal, the clock a colon, the temperature a degree sign, a service countdown says "in … km", an amount of
/// money its currency, gauge labels are short round numbers). Digit passes (which cannot see those signs) are checked against the
/// text passes. The vehicle's latest known reading is the strongest hint: an odometer never goes back.
/// </summary>
public sealed partial class OdometerExtractor(Lexicon lexicon)
{
    [GeneratedRegex(@"\d+[.,]\d+|(?<!\d)\d{1,2}:\d{2}(?!\d)|-?\d+\s?°")]
    private static partial Regex NotAnOdometer();

    [GeneratedRegex(@"^(\d+)(?:km|mi)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DigitsWord();

    /// <summary>How many further passes seeing the same value count: gauge labels and stray digits turn up in every pass.</summary>
    private const int SupportLimit = 3;

    /// <summary>How far above the latest known reading the next one is trusted to be (a reading within it needs no further doubt).</summary>
    private const long NearLatestKm = 5000;

    /// <summary>Two readings scoring closer than this leave the reader unsure which is right.</summary>
    private const double NearTie = 0.5;

    /// <summary>The part of the confidence a dead heat between two readings costs: what is left stays below the 0.6 the app fills in.</summary>
    private const double DeadHeatDoubt = 0.4;

    private sealed record Group(OcrPage Page, long Value, int Digits, Box Box, double Confidence, bool Km, bool NotOdometer, bool Money)
    {
        public string Text => Value.ToString(CultureInfo.InvariantCulture);
    }

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
        // The digit passes see no words: what every text pass found to be an amount of money is none of the odometer's on any pass, and
        // what it found to be a countdown or a trip reading counts against it on every pass.
        var fromText = all.Where(g => textPages.Contains(g.Page)).GroupBy(g => g.Value).ToList();
        excluded.UnionWith(fromText.Where(same => same.All(g => g.Money)).Select(same => same.First().Text));
        var notOdometer = fromText.Where(same => same.All(g => g.NotOdometer)).Select(same => same.Key).ToHashSet();
        all = all.Select(g => !g.NotOdometer && notOdometer.Contains(g.Value) ? g with { NotOdometer = true } : g).ToList();
        // The labels of the gauges: short round numbers (20, 40 … 100, 120). Two side by side are often read as one ("100120").
        var labels = all.Where(g => g.Digits is 2 or 3 && g.Value > 0 && g.Value % 10 == 0).Select(g => g.Value).ToHashSet();
        var groups = all
            .Where(g => g.Digits is >= 3 and <= 7 && !excluded.Contains(g.Text))
            .Where(g => g.Digits == 3 || labels.Count < 3 || NearLatest(g.Value, hints.LastOdometer) || !GluedLabels(g.Text, labels))
            .Where(g => Plausible(g.Value, hints.LastOdometer))
            .ToList();
        if (groups.Count == 0) return null;

        // Other passes seeing the same value confirm it, up to a point: the labels of a gauge are seen by every pass too. A pass that
        // lost the first or last digit (a dark edge of the display) confirms the one that read them all, but only lends it its votes:
        // the shorter reading stays a rival, because a pass that gained a stray digit looks just the same.
        var values = groups
            .GroupBy(g => g.Value)
            .Select(same => new Candidate(same.MaxBy(g => Score(g, kmOnPage, hints))!, same.Select(g => g.Page).ToHashSet(), same.Max(g => g.Confidence)))
            .ToList();
        var byText = values.ToDictionary(v => v.Best.Text);
        var ranked = values
            .Select(v => (v.Best, Pages: v.Pages.Union(v.Parts(byText).SelectMany(part => part.Pages)).Count()))
            .Select(x => (Group: x.Best, Score: Score(x.Best, kmOnPage, hints) + 0.4 * Math.Min(SupportLimit, x.Pages - 1)))
            .OrderByDescending(x => x.Score)
            .ToList();
        var best = ranked[0];
        if (best.Score < 0.8) return null;
        // Another reading nearly as likely (a misread of the same digits by another pass, say) leaves the reader unsure, whichever it
        // picks. The doubt is taken off the confidence itself, not off the score: a latest reading near both readings adds the same to
        // each, which puts the score on the flat end of the confidence curve, where a dead heat would still come out sure. A dead heat
        // costs two fifths of the confidence, which brings it below what the app fills in; the cost fades to nothing at half a point.
        var margin = ranked.Count > 1 ? best.Score - ranked[1].Score : double.MaxValue;
        var confidence = Math.Round(Confidence.From(best.Score, best.Group.Confidence, 1.5) * (1 - DeadHeatDoubt * (1 - Math.Min(margin, NearTie) / NearTie)), 2);
        return new ReadField(FieldNames.Odometer, best.Group.Text, confidence, FieldSources.Ocr);
    }

    /// <summary>One value with the passes that saw it; <see cref="Confidence"/> is the OCR's best certainty of it.</summary>
    private sealed record Candidate(Group Best, HashSet<OcrPage> Pages, double Confidence)
    {
        private string Text => Best.Text;

        /// <summary>
        /// The values that are this one with its first or last digits lost (at least four digits left), read with no more certainty
        /// than the whole: found by looking up its own prefixes and suffixes, not by comparing it with every other value.
        /// </summary>
        public IEnumerable<Candidate> Parts(Dictionary<string, Candidate> all)
        {
            for (var length = 4; length < Text.Length; length++)
            {
                if (all.TryGetValue(Text[..length], out var prefix) && Confidence >= prefix.Confidence) yield return prefix;
                if (all.TryGetValue(Text[^length..], out var suffix) && Confidence >= suffix.Confidence) yield return suffix;
            }
        }
    }

    /// <summary>Whether the digits are gauge labels run together, each 2 or 3 digits long ("100120" where 100 and 120 are labels).</summary>
    private static bool GluedLabels(string digits, HashSet<long> labels)
    {
        if (digits.Length == 0) return true;
        return digits[0] != '0' // a label is never printed with a leading zero
            && ((digits.Length >= 2 && labels.Contains(long.Parse(digits[..2], CultureInfo.InvariantCulture)) && GluedLabels(digits[2..], labels))
                || (digits.Length >= 3 && labels.Contains(long.Parse(digits[..3], CultureInfo.InvariantCulture)) && GluedLabels(digits[3..], labels)));
    }

    /// <summary>Whether the value is just above the latest known reading, as the next reading of the vehicle is (see <see cref="Score"/>).</summary>
    private static bool NearLatest(long value, long? last) => last is { } l && value >= l && value - l <= NearLatestKm;

    /// <summary>
    /// With the latest known reading, an odometer below it (it never goes back) or more than 100 000 above it (between two logs) is a
    /// misread: no reading is better than a confident wrong one.
    /// </summary>
    private static bool Plausible(long value, long? last) => last is not { } l || (value >= l && value - l <= 100_000);

    private static double Score(Group g, bool kmOnPage, ReadHints hints)
    {
        var score = g.Digits switch { 3 => -0.8, 4 => 0.2, 5 => 0.8, 6 => 1.0, _ => 0.6 };
        var scale = g.Page.MedianWordHeight > 0 ? (double)g.Box.Height / g.Page.MedianWordHeight : 1;
        score += 0.6 * Math.Clamp(scale - 1, 0, 1); // bigger than the rest helps, but the labels of a speedometer are big too
        score += 0.8 * (Math.Clamp(g.Confidence, 0, 100) / 100 - 0.5); // what the OCR itself doubts counts less
        score += g.Km && !g.NotOdometer ? 1.0 : kmOnPage ? 0.3 : 0; // the km of a countdown or a trip meter says nothing about the odometer
        score -= g.NotOdometer ? 1.5 : 0;
        score -= g.Digits == 4 && g.Value / 100 <= 23 && g.Value % 100 <= 59 ? 1.2 : 0; // a clock whose colon the OCR lost ("02:06" → "0206")
        score -= g.Value % 100 == 0 ? 0.6 : 0; // round numbers are gauge labels and service intervals far more often than readings
        if (hints.LastOdometer is { } last) score += g.Value - last <= NearLatestKm ? 2.5 : g.Value - last <= 50_000 ? 0.5 : -0.5;
        return score;
    }

    /// <summary>
    /// Runs of digit words on a line, joining neighbours a small gap apart ("123 456" on some clusters), with what the words around
    /// them say: an odometer unit anywhere on the line, a trip or countdown word only right beside the number. The digits of one
    /// number are all one size: a gauge label next to the odometer is never part of it.
    /// </summary>
    private IEnumerable<Group> Groups(OcrPage page, OcrLine line)
    {
        var run = new List<(OcrWord Word, string Digits)>();
        string? folded = null; // only lines with digits need their words looked up

        IEnumerable<Group> Flush()
        {
            if (run.Count == 0) yield break;
            var digits = string.Concat(run.Select(r => r.Digits));
            if (digits.Length <= 18 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                var box = Box.Union(run.Select(r => r.Word.Box));
                // A word beside the number says what it is ("Service in 4800 km", "4800 km múlva"); the others on the same row (another
                // gauge, a warning text) say nothing about it.
                var beside = Folding.Fold(string.Join(' ', line.Words.Where(w => Beside(box, w.Box)).Select(w => w.Text)));
                folded ??= Folding.Fold(line.Text);
                yield return new Group(page, value, digits.Length, box, run.Min(r => r.Word.Confidence), lexicon.HasOdometerWord(folded),
                    lexicon.HasTripWord(beside) || (digits.Length <= 5 && lexicon.HasCountdownWord(beside)), // a distance still to go is never six digits long
                    !lexicon.HasOdometerWord(beside) && Currencies.Find(beside).Any()); // an amount of money has its currency beside it
            }
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

    /// <summary>Whether a word stands within three digit heights of the number, on either side.</summary>
    private static bool Beside(Box number, Box word) => Math.Max(number.Left, word.Left) - Math.Min(number.Right, word.Right) <= 3 * number.Height;

    private static bool Continues(Box previous, Box next)
    {
        var height = Math.Max(previous.Height, next.Height);
        return next.Left - previous.Right <= height && Math.Abs(next.Height - previous.Height) <= 0.35 * height;
    }
}
