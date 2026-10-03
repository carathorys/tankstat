using System.Globalization;
using System.Text.RegularExpressions;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.Extraction;

/// <summary>
/// Reads a receipt: the total, its currency and its date on every receipt, the litres and the price per litre on a fuel receipt,
/// and the merchant (a suggestion for the title) on other receipts. Every number on the page is a candidate for every field; the
/// scorer rates it by what surrounds it, and on fuel receipts "litres × price per litre ≈ total" picks the three that fit together.
/// </summary>
public sealed partial class ReceiptExtractor(Lexicon lexicon, ICandidateScorer scorer)
{
    [GeneratedRegex(@"^\s?(?:l|ltr|liter|litre|liters|litres|gal|gallon|gallons)(?![a-z/])")]
    private static partial Regex VolumeUnitAfter();

    [GeneratedRegex(@"^\s?(?:[a-z€$£]{0,3}\s?)/\s?(?:l|ltr|liter|litre|gal)(?![a-z])")]
    private static partial Regex PerUnitAfter();

    [GeneratedRegex(@"[x×*@]\s?$")]
    private static partial Regex MultiplyBefore();

    // "38,52 l x 640,9", "38,52 x 640,9": the first number of a "litres times price" line, even when the OCR lost the unit.
    [GeneratedRegex(@"^\s?(?:l|ltr|liter|litre|gal)?\s?[x×*@]\s?\d")]
    private static partial Regex MultiplyAfter();

    private sealed record Candidate(NumberToken Token, NumberReading Reading, string Field, double Score);

    public IReadOnlyList<ReadField> Extract(OcrPage page, string kind, ReadHints hints)
    {
        var folded = page.Lines.Select(l => Folding.Fold(l.Text)).ToArray();
        var tokens = page.Lines.SelectMany((line, i) => LineScan.Numbers(line, i, hints.Locale, hints.Today)).ToList();
        var fields = new List<ReadField>();

        var currency = Currency(page.Lines.Where(l => l.Confidence >= DocumentClassifier.SureLine).Select(l => Folding.Fold(l.Text)).ToArray(), hints);
        if (currency is not null) fields.Add(currency);

        var candidates = Candidates(page, folded, tokens);
        if (kind == DocumentKinds.FuelReceipt) fields.AddRange(FuelAmounts(candidates, Currencies.Decimals(currency?.Value ?? "EUR")));
        else if (Top(candidates, FieldNames.Total, 1.0).FirstOrDefault() is { } total) fields.Add(Field(FieldNames.Total, total, 2));

        if (Date(page, folded, hints.Today) is { } date) fields.Add(date);
        if (kind == DocumentKinds.ExpenseReceipt && Title(page, folded) is { } title) fields.Add(title);
        return fields;
    }

    private List<Candidate> Candidates(OcrPage page, string[] folded, List<NumberToken> tokens)
    {
        var withNumbers = tokens.Select(t => t.LineIndex).ToHashSet();
        var total = folded.Select(lexicon.TotalWeight).ToArray();
        var notTotal = folded.Select(lexicon.NotTotalWeight).ToArray();
        var fuel = folded.Select(lexicon.HasFuelWord).ToArray();
        var unitPriceWord = folded.Select(lexicon.HasUnitPriceWord).ToArray();

        var marked = tokens.Select(t => (Token: t, Volume: VolumeUnitAfter().IsMatch(t.After) || MultiplyAfter().IsMatch(t.After), PerUnit: PerUnitAfter().IsMatch(t.After))).ToList();
        // On a "litres x price" line the number after the sign is the price per litre, even when its unit came out garbled ("Fi/t").
        var litreLines = marked.Where(m => m.Volume).Select(m => m.Token.LineIndex).ToHashSet();
        marked = marked.Select(m => m with { PerUnit = m.PerUnit || (!m.Volume && litreLines.Contains(m.Token.LineIndex) && MultiplyBefore().IsMatch(m.Token.Before)) }).ToList();
        var largest = marked.Where(m => !m.Volume && !m.PerUnit).SelectMany(m => m.Token.Readings).Select(r => r.Value).DefaultIfEmpty(0).Max();

        var candidates = new List<Candidate>();
        foreach (var (token, volume, perUnit) in marked)
        {
            var line = token.LineIndex;
            var labelAbove = line > 0 && !withNumbers.Contains(line - 1) ? total[line - 1] : 0;
            var fontScale = page.MedianWordHeight > 0 ? (double)token.Box.Height / page.MedianWordHeight : 1;
            var rightAligned = page.Width > 0 && page.Width - token.Box.Right < 0.2 * page.Width;
            var vertical = page.Height > 0 ? token.Box.CenterY / page.Height : 0.5;
            var currencyAdjacent = CurrencyAdjacent(token);
            var multiply = MultiplyBefore().IsMatch(token.Before);

            for (var r = 0; r < token.Readings.Count; r++)
            {
                var reading = token.Readings[r];
                var repeats = tokens.Count(o => !ReferenceEquals(o, token) && o.Readings.Any(x => x.Value == reading.Value));
                CandidateFeatures Features(bool plausible) => new(
                    total[line], labelAbove, notTotal[line], currencyAdjacent, volume, perUnit, multiply, unitPriceWord[line], fuel[line],
                    fontScale, rightAligned, vertical, repeats, reading.Value == largest && !volume && !perUnit, reading.Decimals, plausible);

                // The locale's usual reading of an ambiguous number comes first; the other one has to earn its place.
                var penalty = 0.3 * r;
                var value = reading.Value;
                candidates.Add(new(token, reading, FieldNames.Total,
                    scorer.Score(FieldNames.Total, Features(value > 0 && value < 10_000_000 && reading.Decimals <= 2)) - penalty));
                candidates.Add(new(token, reading, FieldNames.Volume,
                    scorer.Score(FieldNames.Volume, Features(value >= 0.3m && value <= 400 && reading.Decimals <= 3)) - penalty));
                candidates.Add(new(token, reading, FieldNames.UnitPrice,
                    scorer.Score(FieldNames.UnitPrice, Features(value > 0 && value < 100_000 && reading.Decimals <= 3)) - penalty));
            }
        }
        return candidates;
    }

    private static bool CurrencyAdjacent(NumberToken token)
    {
        var before = token.Before.TrimEnd();
        var after = token.After.TrimStart();
        return Currencies.Find(after).Any(c => c.Index == 0) || Currencies.Find(before).Any(c => c.Index + c.Length == before.Length);
    }

    /// <summary>The best candidates of a field (one reading per number), strongest first.</summary>
    private static List<Candidate> Top(List<Candidate> all, string field, double threshold) =>
        all.Where(c => c.Field == field && c.Score >= threshold)
            .GroupBy(c => c.Token)
            .Select(g => g.MaxBy(c => c.Score)!)
            .OrderByDescending(c => c.Score)
            .Take(3)
            .ToList();

    private static IEnumerable<ReadField> FuelAmounts(List<Candidate> all, int currencyDecimals)
    {
        var totals = Top(all, FieldNames.Total, 1.0);
        var volumes = Top(all, FieldNames.Volume, 2.0);
        var prices = Top(all, FieldNames.UnitPrice, 2.0);

        (Candidate Total, Candidate Volume, Candidate Price)? fit = null;
        var best = double.MinValue;
        foreach (var t in totals)
            foreach (var v in volumes)
                foreach (var p in prices)
                {
                    if (t.Token == v.Token || t.Token == p.Token || v.Token == p.Token) continue;
                    if (!Fits(t.Reading.Value, v.Reading.Value * p.Reading.Value, currencyDecimals)) continue;
                    var score = t.Score + v.Score + p.Score;
                    if (score > best) (best, fit) = (score, (t, v, p));
                }
        if (fit is { } f)
            return [Field(FieldNames.Total, f.Total, 2, 1.5), Field(FieldNames.Volume, f.Volume, 3, 1.5), Field(FieldNames.UnitPrice, f.Price, 3, 1.5)];

        var total = totals.FirstOrDefault();
        var volumeC = volumes.FirstOrDefault(c => c.Token != total?.Token);
        var price = prices.FirstOrDefault(c => c.Token != total?.Token && c.Token != volumeC?.Token);
        var fields = new List<ReadField>();
        // All three read but they do not fit: one of them is misread, most likely the litres or the price (the total is printed
        // several times). Their confidence drops below what the app fills in, so the user types them rather than fixing a wrong one.
        var misfit = total is not null && volumeC is not null && price is not null ? 0.6 : 1;
        if (total is not null) fields.Add(Field(FieldNames.Total, total, 2));
        if (volumeC is not null) fields.Add(Scaled(Field(FieldNames.Volume, volumeC, 3), misfit));
        if (price is not null) fields.Add(Scaled(Field(FieldNames.UnitPrice, price, 3), misfit));

        // Two of the three give the third, with less confidence than either.
        double Conf(params Candidate[] from) => Math.Round(from.Min(c => Confidence.From(c.Score, c.Token.Confidence)) * 0.85, 2);
        if (total is not null && volumeC is not null && price is null && volumeC.Reading.Value > 0)
            fields.Add(new ReadField(FieldNames.UnitPrice, Numbers.Format(total.Reading.Value / volumeC.Reading.Value, 3), Conf(total, volumeC), FieldSources.Derived));
        else if (total is null && volumeC is not null && price is not null)
            fields.Add(new ReadField(FieldNames.Total, Numbers.Format(volumeC.Reading.Value * price.Reading.Value, currencyDecimals), Conf(volumeC, price), FieldSources.Derived));
        else if (total is not null && volumeC is null && price is not null && price.Reading.Value > 0)
            fields.Add(new ReadField(FieldNames.Volume, Numbers.Format(total.Reading.Value / price.Reading.Value, 2), Conf(total, price), FieldSources.Derived));
        return fields;
    }

    /// <summary>Whether a total and litres × price per litre agree: within 1.5 %, or one and a half of the currency's smallest unit.</summary>
    public static bool Fits(decimal total, decimal product, int currencyDecimals)
    {
        var unit = currencyDecimals == 0 ? 1m : 0.01m;
        return Math.Abs(total - product) <= Math.Max(total * 0.015m, unit * 1.5m);
    }

    private static ReadField Scaled(ReadField field, double factor) => factor == 1 ? field : field with { Confidence = Math.Round(field.Confidence * factor, 2) };

    private static ReadField Field(string name, Candidate c, int maxDecimals, double bonus = 0) =>
        new(name, Numbers.Format(c.Reading.Value, maxDecimals), Confidence.From(c.Score + bonus, c.Token.Confidence), FieldSources.Ocr);

    /// <summary>
    /// The currency the amounts are in: the marker printed next to the most of them, on lines the OCR is sure of. A marker next to
    /// no number at all ("Ft" in a column heading, or a stray "£" the OCR made of a photo that is no receipt) is a guess the app must
    /// not fill in on its own.
    /// </summary>
    private static ReadField? Currency(string[] folded, ReadHints hints)
    {
        var found = folded.SelectMany(f => Currencies.Find(f).Select(c => (c.Code, NextToANumber: Currencies.NextToANumber(f, c.Index, c.Length))))
            .GroupBy(c => c.Code)
            .OrderByDescending(g => g.Count(c => c.NextToANumber))
            .ThenByDescending(g => g.Count())
            .ThenByDescending(g => g.Key == hints.Currency)
            .FirstOrDefault();
        if (found is not null)
        {
            var confidence = found.Count(c => c.NextToANumber) switch { >= 2 => 0.9, 1 => 0.75, _ => 0.5 };
            return new ReadField(FieldNames.Currency, found.Key, confidence, FieldSources.Ocr);
        }
        return hints.Currency is { Length: 3 } hint ? new ReadField(FieldNames.Currency, hint.ToUpperInvariant(), 0.45, FieldSources.Hint) : null;
    }

    private ReadField? Date(OcrPage page, string[] folded, DateOnly today)
    {
        var candidates = new List<(DateOnly Date, double Score, double Ocr)>();
        for (var i = 0; i < page.Lines.Count; i++)
        {
            var text = page.Lines[i].Text;
            var matches = Dates.Find(text, today);
            foreach (var m in matches)
            {
                var ambiguous = matches.Count(o => o.Index == m.Index) > 1;
                var score = (lexicon.HasDateWord(folded[i]) ? 1 : 0) + (Dates.HasTime(text) ? 0.5 : 0)
                    + Math.Max(0, 1 - (today.DayNumber - m.Date.DayNumber) / 365.0) - (ambiguous ? 0.3 : 0);
                candidates.Add((m.Date, score, page.Lines[i].Confidence));
            }
        }
        if (candidates.Count == 0) return null;
        var best = candidates.MaxBy(c => c.Score);
        var unique = candidates.Select(c => c.Date).Distinct().Count() == 1;
        var confidence = Math.Round((unique ? 0.85 : 0.7) * (0.6 + 0.4 * best.Ocr / 100), 2);
        return new ReadField(FieldNames.Date, best.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), confidence, FieldSources.Ocr);
    }

    /// <summary>The merchant: the most prominent line near the top that is mostly letters and not receipt boilerplate.</summary>
    private ReadField? Title(OcrPage page, string[] folded)
    {
        var topLimit = page.Height > 0 ? page.Height * 0.3 : double.MaxValue;
        var best = page.Lines
            .Select((line, index) => (Line: line, Index: index))
            .Where(x => x.Index < 6 && (x.Line.Box.Top <= topLimit || x.Index < 3) && LooksLikeName(x.Line.Text, folded[x.Index]))
            // Word height, not the line's box: on a slightly turned photo a long line spans more pixels without being any bigger.
            .Select(x => (x.Line, x.Index, Score: (page.MedianWordHeight > 0 ? x.Line.Words.Average(w => w.Box.Height) / page.MedianWordHeight : 1) - 0.15 * x.Index))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        if (best.Line is null) return null;
        var text = string.Join(' ', best.Line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(':', ',', '-', '*', ' ');
        if (text.Length > 120) text = text[..120].TrimEnd();
        // The shop's name heads the receipt: a name on the first lines is a fair suggestion, one further down a guess.
        var confidence = Math.Round((best.Index <= 1 ? 0.7 : 0.5) * (0.6 + 0.4 * best.Line.Confidence / 100), 2);
        return text.Length < 3 ? null : new ReadField(FieldNames.Title, text, confidence, FieldSources.Ocr);
    }

    /// <summary>Mostly letters, hardly any digits (that is an address or a tax number), and not receipt boilerplate.</summary>
    private bool LooksLikeName(string text, string folded)
    {
        var letters = text.Count(char.IsLetter);
        var visible = text.Count(c => !char.IsWhiteSpace(c));
        return letters >= 3 && letters >= 0.6 * visible && text.Count(char.IsDigit) < 2
            && !lexicon.IsTitleNoise(folded) && lexicon.TotalWeight(folded) == 0 && !Currencies.Find(folded).Any();
    }
}
