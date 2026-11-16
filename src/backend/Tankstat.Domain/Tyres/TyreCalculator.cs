namespace Tankstat.Domain.Tyres;

/// <summary>One odometer reading of the vehicle, whatever recorded it.</summary>
public readonly record struct ReadingPoint(DateOnly Date, long Value);

/// <summary>The car's odometer on a day: <see cref="Approximate"/> when it was worked out rather than read that day.</summary>
public readonly record struct OdometerOnDay(long Value, bool Approximate);

/// <summary>How an installation's distance was found.</summary>
public enum InstallationMeasure
{
    /// <summary>From the odometer on both ends.</summary>
    Measured,

    /// <summary>It starts before the vehicle's first reading: counted from that reading.</summary>
    FromFirstReading,

    /// <summary>No reading lies inside it (before the fuel log, or no reading after it went on yet): 0.</summary>
    NotMeasured,
}

/// <summary>An installation's distance, never negative.</summary>
public readonly record struct InstallationDistance(long Distance, bool Approximate, InstallationMeasure Measure);

/// <summary>A set's figures: the total (rough figure plus installations) and the front/rear share of its two pairs.</summary>
/// <param name="PairOneFront">What pair 1 ran at the front (pair 2 ran this at the rear).</param>
/// <param name="PairOneRear">What pair 1 ran at the rear (pair 2 ran this at the front).</param>
/// <param name="PairsUnknown">What ran in installations without recorded pairs.</param>
public sealed record TyreSetFigures(long Distance, bool Approximate, long Measured, long PairOneFront, long PairOneRear, long PairsUnknown, RoughSuggestion? Suggestion);

/// <summary>A single tyre's figures (#289): the total (rough figure plus places) and what it ran at the front, at the rear, and at no corner recorded.</summary>
public sealed record TyreFigures(long Distance, bool Approximate, long Measured, long Front, long Rear, long Unknown, RoughSuggestion? Suggestion);

/// <summary>
/// The installations now measure time the rough figure stood for (past swaps were entered): <see cref="Distance"/> would keep the total as it
/// was, standing for the time before <see cref="Until"/>. It is suggested to the user, never applied by itself.
/// </summary>
public sealed record RoughSuggestion(long Distance, DateOnly Until, long MeasuredBefore);

/// <summary>Days of the calendar, both ends counted.</summary>
public readonly record struct DayRange(DateOnly From, DateOnly Until);

/// <summary>
/// What a used set probably ran before it was tracked (#291): the car's distance in the set's usual months, as the fuel log measured it.
/// Always approximate, and only ever offered: the person takes it or types their own.
/// </summary>
/// <param name="Distance">The sum, rounded to 100 like a <see cref="RoughSuggestion"/>.</param>
/// <param name="From">The first day counted; null when nothing was.</param>
/// <param name="NotCovered">The days of its months before the car's first reading, which no figure can be worked out for.</param>
/// <param name="LeftOutDays">The days of its months a recorded installation covers: the car ran on those tyres (the owner's decision 47).</param>
/// <param name="Readings">Whether the car has a reading at all; without one nothing can be estimated.</param>
public sealed record TyreRoughEstimate(long Distance, DateOnly? From, IReadOnlyList<DayRange> NotCovered, int LeftOutDays, bool Readings);

/// <summary>
/// Pure arithmetic of tyre distances (#287), worked out on every read from the vehicle's odometer readings, nothing stored. The odometer
/// on a day is the highest reading of that day (the tie-break of the latest reading), else a straight line between the nearest readings
/// before and after it (approximate), else after the latest reading the latest one (approximate), and before the first reading it is not
/// known. A worked-out value is never saved as a reading.
/// </summary>
public static class TyreCalculator
{
    /// <param name="readings">The vehicle's live readings, any order.</param>
    public static IReadOnlyList<ReadingPoint> Sorted(IEnumerable<ReadingPoint> readings) =>
        [.. readings.OrderBy(r => r.Date).ThenBy(r => r.Value)];

    /// <param name="sorted">From <see cref="Sorted"/>.</param>
    public static OdometerOnDay? OdometerOn(IReadOnlyList<ReadingPoint> sorted, DateOnly day)
    {
        if (sorted.Count == 0 || day < sorted[0].Date) return null;
        ReadingPoint? before = null, after = null;
        long? sameDay = null;
        foreach (var r in sorted)
        {
            if (r.Date == day) sameDay = sameDay is { } s ? Math.Max(s, r.Value) : r.Value;
            else if (r.Date < day) before = r;
            else { after = r; break; }
        }
        if (sameDay is { } exact) return new OdometerOnDay(exact, false);
        if (after is not { } next) return new OdometerOnDay(before!.Value.Value, true);
        var prev = before!.Value;
        var span = next.Date.DayNumber - prev.Date.DayNumber;
        var part = day.DayNumber - prev.Date.DayNumber;
        return new OdometerOnDay(prev.Value + (long)Math.Round((next.Value - prev.Value) * (double)part / span, MidpointRounding.AwayFromZero), true);
    }

    /// <summary>
    /// The distance of the installation from <paramref name="from"/> to <paramref name="until"/> (null: still on the car, up to the latest reading,
    /// which then counts as exact). A pair of readings that falls (a reading a photo filled in is not checked) gives 0, never less.
    /// </summary>
    public static InstallationDistance Distance(IReadOnlyList<ReadingPoint> sorted, DateOnly from, DateOnly? until)
    {
        if (sorted.Count == 0) return new InstallationDistance(0, false, InstallationMeasure.NotMeasured);
        var latest = sorted[^1];
        OdometerOnDay end;
        if (until is { } u)
        {
            if (OdometerOn(sorted, u) is not { } e) return new InstallationDistance(0, false, InstallationMeasure.NotMeasured);
            end = e;
        }
        else
        {
            if (latest.Date < from) return new InstallationDistance(0, false, InstallationMeasure.NotMeasured);
            end = new OdometerOnDay(OdometerOn(sorted, latest.Date)!.Value.Value, false);
        }
        var start = OdometerOn(sorted, from);
        var measure = InstallationMeasure.Measured;
        if (start is null && sorted[0].Date >= (until ?? latest.Date))
            return new InstallationDistance(0, false, InstallationMeasure.NotMeasured); // the fuel log starts on its last day: no reading inside it
        if (start is null)
        {
            start = new OdometerOnDay(OdometerOn(sorted, sorted[0].Date)!.Value.Value, false);
            measure = InstallationMeasure.FromFirstReading;
        }
        return new InstallationDistance(Math.Max(0, end.Value - start.Value.Value), end.Approximate || start.Value.Approximate, measure);
    }

    /// <summary>
    /// The estimate of a used set's rough figure (#291): the days of <paramref name="months"/> from the first day of
    /// <paramref name="since"/>'s month (*in use since* is month-level, decision 48) up to <paramref name="until"/>, the first day the figure
    /// does not cover; less the days any live installation covers, this set's own too (decision 47); each run of days left counts the car's
    /// odometer at its end minus at its start. A run before the car's first reading is not covered; after the latest reading it counts 0.
    /// </summary>
    /// <param name="installations">Every live installation of the vehicle, of any set; one still on the car covers up to <paramref name="today"/>.</param>
    public static TyreRoughEstimate Estimate(
        IReadOnlyList<ReadingPoint> sorted, IEnumerable<(DateOnly From, DateOnly? Until)> installations, TyreMonths months, DateOnly since, DateOnly until, DateOnly today)
    {
        var covered = installations.Select(i => (i.From, Until: i.Until ?? today)).ToList();
        var first = sorted.Count > 0 ? sorted[0].Date : (DateOnly?)null;
        var notCovered = new List<DayRange>();
        long sum = 0;
        var leftOut = 0;
        DateOnly? from = null, runStart = null;

        void Close(DateOnly end) // the run [runStart, end)
        {
            if (runStart is not { } start) return;
            runStart = null;
            if (first is not { } firstReading) return;
            if (start < firstReading) notCovered.Add(new DayRange(start, (end <= firstReading ? end : firstReading).AddDays(-1)));
            if (end <= firstReading) return;
            var counted = start < firstReading ? firstReading : start;
            from ??= counted;
            sum += Math.Max(0, OdometerOn(sorted, end)!.Value.Value - OdometerOn(sorted, counted)!.Value.Value);
        }

        for (var day = new DateOnly(since.Year, since.Month, 1); day < until; day = day.AddDays(1))
        {
            var free = months.Has(day.Month);
            if (free && covered.Any(c => c.From <= day && day < c.Until))
            {
                leftOut++;
                free = false;
            }
            if (free) runStart ??= day;
            else Close(day);
        }
        Close(until);
        return new TyreRoughEstimate((long)Math.Round(sum / 100.0, MidpointRounding.AwayFromZero) * 100, from, notCovered, leftOut, first is not null);
    }

    /// <summary>A whole set's figures from its live installations and its rough figure.</summary>
    public static TyreSetFigures SetFigures(IReadOnlyList<ReadingPoint> sorted, TyreSet set, IEnumerable<TyreInstallation> installations)
    {
        var f = Figures(sorted, set.RoughDistance, set.RoughUntil, installations.Select(s => (s.From, s.Until, s.PairOneFront switch { true => Side.Front, false => Side.Rear, _ => Side.Unknown })));
        return new TyreSetFigures(f.Distance, f.Approximate, f.Measured, f.Front, f.Rear, f.Unknown, f.Suggestion);
    }

    /// <summary>
    /// A single tyre's figures (#289) from its live places and its rough figure: front is a front corner or <see cref="TyreCorner.Front"/>,
    /// rear the other three, a place without a corner unknown. The rough suggestion works as a set's.
    /// </summary>
    public static TyreFigures TyreFigures(IReadOnlyList<ReadingPoint> sorted, Tyre tyre, IEnumerable<TyrePlace> places)
    {
        var f = Figures(sorted, tyre.RoughDistance, tyre.RoughUntil, places.Select(p => (p.From, p.Until, p.Corner switch
        {
            { } c when c.IsFront() => Side.Front,
            { } c when c.IsRear() => Side.Rear,
            _ => Side.Unknown,
        })));
        return new TyreFigures(f.Distance, f.Approximate, f.Measured, f.Front, f.Rear, f.Unknown, f.Suggestion);
    }

    private enum Side
    {
        Front,
        Rear,
        Unknown,
    }

    private sealed record Totals(long Distance, bool Approximate, long Measured, long Front, long Rear, long Unknown, RoughSuggestion? Suggestion);

    private static Totals Figures(IReadOnlyList<ReadingPoint> sorted, long rough, DateOnly? roughUntil, IEnumerable<(DateOnly From, DateOnly? Until, Side Side)> spans)
    {
        long measured = 0, front = 0, rear = 0, unknown = 0, before = 0;
        var approximate = rough > 0;
        DateOnly? firstMeasured = null;
        foreach (var s in spans)
        {
            var d = Distance(sorted, s.From, s.Until);
            measured += d.Distance;
            approximate |= d.Approximate || d.Measure == InstallationMeasure.FromFirstReading;
            switch (s.Side)
            {
                case Side.Front: front += d.Distance; break;
                case Side.Rear: rear += d.Distance; break;
                default: unknown += d.Distance; break;
            }
            if (d.Measure == InstallationMeasure.NotMeasured) continue;
            var measuredFrom = d.Measure == InstallationMeasure.FromFirstReading ? sorted[0].Date : s.From;
            if (firstMeasured is null || measuredFrom < firstMeasured) firstMeasured = measuredFrom;
            if (roughUntil is { } until && measuredFrom < until)
            {
                var cut = s.Until is { } u && u < until ? u : until;
                before += Distance(sorted, s.From, cut).Distance;
            }
        }
        RoughSuggestion? suggestion = before > 0 && rough > 0 && firstMeasured is { } first
            ? new RoughSuggestion(Math.Max(0, (long)Math.Round((rough - before) / 100.0, MidpointRounding.AwayFromZero) * 100), first, before)
            : null;
        return new Totals(rough + measured, approximate, measured, front, rear, unknown, suggestion);
    }
}
