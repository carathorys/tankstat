using Tankstat.Reader.Core;

namespace Tankstat.Reader.Synthetic;

/// <summary>
/// Generates photos with known values: fuel receipts, other receipts and dashboards in equal shares, in Hungarian, English and German.
/// The same seed always gives the same cases, so accuracy can be compared between versions of the rules (and, later, used to train).
/// </summary>
public static class SyntheticGenerator
{
    /// <summary>A fixed "today" by default, so generated dates (and their plausibility) do not change from one day to the next.</summary>
    public static readonly DateOnly DefaultToday = new(2026, 10, 1);

    public static IEnumerable<SyntheticCase> Generate(int count, int seed = 1, DateOnly? today = null, string? kind = null)
    {
        for (var i = 0; i < count; i++)
        {
            var caseKind = kind ?? DocumentKinds.All[i % DocumentKinds.All.Count];
            // Not HashCode.Combine: it is randomised per process, and the same seed must give the same photos on every run.
            var caseSeed = unchecked(seed * 1_000_003 + i * 7_919 + DocumentKinds.All.ToList().IndexOf(caseKind) * 104_729);
            yield return Make(i, caseKind, new Random(caseSeed), today ?? DefaultToday);
        }
    }

    private static SyntheticCase Make(int index, string kind, Random random, DateOnly today)
    {
        var name = $"synthetic-{index + 1:0000}-{kind}";
        if (kind == DocumentKinds.Odometer)
        {
            var (display, words, odometer) = Dashboards.Paint(random);
            using (display)
            {
                var (image, width, height, placed) = Photos.Dashboard(display, words, random);
                long? last = random.Next(10) < 7 ? Math.Max(0, odometer - random.Next(50, 900)) : null;
                return new SyntheticCase(name, kind, new ReadHints("hu", last, null, today), new Dictionary<string, string> { [FieldNames.Odometer] = odometer.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    image, width, height, placed);
            }
        }

        var content = Receipts.Make(random, kind == DocumentKinds.FuelReceipt, today);
        var (paper, printed) = Receipts.Print(content, random);
        using (paper)
        {
            var (image, width, height, placed) = Photos.Receipt(paper, printed, random);
            return new SyntheticCase(name, content.Kind, new ReadHints(content.Locale, null, null, today), content.Expected, image, width, height, placed);
        }
    }
}
