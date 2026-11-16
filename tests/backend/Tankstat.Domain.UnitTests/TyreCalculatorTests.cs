using Tankstat.Domain.Tyres;

namespace Tankstat.Domain.UnitTests;

public class TyreCalculatorTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static readonly IReadOnlyList<ReadingPoint> Log = TyreCalculator.Sorted([
        new(D(2024, 6, 1), 118_000), new(D(2025, 3, 29), 128_150), new(D(2025, 4, 8), 129_150), new(D(2026, 11, 10), 148_200),
    ]);

    [Fact]
    public void Odometer_IsTheReadingOfTheDay_TheHighestWhenThereAreSeveral()
    {
        var sorted = TyreCalculator.Sorted([new(D(2026, 1, 5), 1000), new(D(2026, 1, 5), 1060), new(D(2026, 1, 1), 900)]);
        Assert.Equal(new OdometerOnDay(1060, false), TyreCalculator.OdometerOn(sorted, D(2026, 1, 5)));
    }

    [Fact]
    public void Odometer_BetweenReadings_IsAStraightLine_MarkedApproximate()
    {
        // 29 Mar 2025 at 128,150 and 8 Apr at 129,150: 100 km a day.
        Assert.Equal(new OdometerOnDay(128_550, true), TyreCalculator.OdometerOn(Log, D(2025, 4, 2)));
    }

    [Fact]
    public void Odometer_BeforeTheFirstReading_IsNotKnown_AfterTheLatest_IsTheLatest_Approximate()
    {
        Assert.Null(TyreCalculator.OdometerOn(Log, D(2024, 5, 31)));
        Assert.Equal(new OdometerOnDay(148_200, true), TyreCalculator.OdometerOn(Log, D(2026, 12, 1)));
        Assert.Null(TyreCalculator.OdometerOn([], D(2026, 1, 1)));
    }

    [Fact]
    public void Distance_OfAInstallationStillOnTheCar_RunsToTheLatestReading_Exactly()
    {
        Assert.Equal(new InstallationDistance(20_050, false, InstallationMeasure.Measured), TyreCalculator.Distance(Log, D(2025, 3, 29), null));
    }

    [Fact]
    public void Distance_UsesTheSameValueForTheBoundaryDay_OnBothSides()
    {
        var off = TyreCalculator.Distance(Log, D(2025, 3, 29), D(2025, 4, 2));
        var on = TyreCalculator.Distance(Log, D(2025, 4, 2), D(2025, 4, 8));
        Assert.Equal(1000, off.Distance + on.Distance); // 128,150 to 129,150 split at the worked-out boundary, nothing lost or counted twice
        Assert.True(off.Approximate && on.Approximate);
    }

    [Fact]
    public void Distance_BeforeTheFuelLog_CountsFromItsFirstReading_OrIsNotMeasured()
    {
        Assert.Equal(new InstallationDistance(10_150, false, InstallationMeasure.FromFirstReading), TyreCalculator.Distance(Log, D(2024, 4, 2), D(2025, 3, 29)));
        Assert.Equal(new InstallationDistance(0, false, InstallationMeasure.NotMeasured), TyreCalculator.Distance(Log, D(2023, 3, 25), D(2023, 11, 12)));
        Assert.Equal(InstallationMeasure.NotMeasured, TyreCalculator.Distance(Log, D(2024, 4, 2), D(2024, 6, 1)).Measure); // the log starts on its last day
    }

    [Fact]
    public void Distance_WithoutAReadingAfterItWentOn_IsNotMeasured()
    {
        Assert.Equal(InstallationMeasure.NotMeasured, TyreCalculator.Distance(Log, D(2026, 11, 20), null).Measure);
        Assert.Equal(InstallationMeasure.NotMeasured, TyreCalculator.Distance([], D(2026, 1, 1), null).Measure);
    }

    [Fact]
    public void Distance_NeverGoesBelowZero_WhenReadingsFall()
    {
        var falling = TyreCalculator.Sorted([new(D(2026, 1, 1), 2000), new(D(2026, 2, 1), 1500)]); // a reading a photo filled in is not checked
        Assert.Equal(0, TyreCalculator.Distance(falling, D(2026, 1, 1), D(2026, 2, 1)).Distance);
    }

    [Fact]
    public void SetFigures_AddTheRoughFigure_AndSplitThePairs()
    {
        var owner = Guid.NewGuid();
        var car = Guid.NewGuid();
        var set = TyreSet.Create(owner, owner, car, "Michelin Primacy 4", TyreSeason.Summer, null, D(2023, 3, 25), 5, 2023, 11_900, D(2026, 3, 28));
        TyreInstallation[] installations = [
            TyreInstallation.Create(owner, owner, car, set.Id, D(2025, 3, 29), D(2025, 4, 8), null),
            TyreInstallation.Create(owner, owner, car, set.Id, D(2025, 4, 8), D(2026, 11, 10), true),
        ];

        var figures = TyreCalculator.SetFigures(Log, set, installations);

        Assert.Equal(11_900 + 1000 + 19_050, figures.Distance);
        Assert.Equal((19_050L, 0L, 1000L), (figures.PairOneFront, figures.PairOneRear, figures.PairsUnknown));
        Assert.True(figures.Approximate); // the rough figure
    }

    [Fact]
    public void SetFigures_SuggestANewRoughFigure_WhenInstallationsMeasureTheTimeItStoodFor()
    {
        var owner = Guid.NewGuid();
        var car = Guid.NewGuid();
        // Added on 10 Nov 2026 with 32,900 km, standing for everything before; then a past swap of 29 Mar 2025 is entered.
        var set = TyreSet.Create(owner, owner, car, "Michelin Primacy 4", TyreSeason.Summer, null, D(2023, 3, 25), null, null, 32_900, D(2026, 11, 10));
        var installation = TyreInstallation.Create(owner, owner, car, set.Id, D(2025, 3, 29), D(2026, 11, 10), null);

        var suggestion = TyreCalculator.SetFigures(Log, set, [installation]).Suggestion;

        Assert.NotNull(suggestion);
        Assert.Equal((20_050L, 12_900L, D(2025, 3, 29)), (suggestion.MeasuredBefore, suggestion.Distance, suggestion.Until));
        Assert.Null(TyreCalculator.SetFigures(Log, set, []).Suggestion);
    }

    // ---- the estimate of a used set's rough figure (#291) ---------------------------------------------------------

    private static readonly TyreMonths SummerMonths = TyreMonths.DefaultFor(TyreSeason.Summer);
    private static readonly TyreMonths WinterMonths = TyreMonths.DefaultFor(TyreSeason.Winter);
    private static readonly DateOnly Today = D(2026, 11, 10);

    /// <summary>#286's worked example: the fuel log from June 2024, a reading on each day a counted run starts or ends.</summary>
    private static readonly IReadOnlyList<ReadingPoint> Example = TyreCalculator.Sorted([
        new(D(2024, 6, 1), 118_000), new(D(2024, 11, 1), 123_900), // June to October 2024: 5,900
        new(D(2025, 4, 1), 127_700), new(D(2025, 4, 15), 128_000), new(D(2025, 11, 1), 137_100), // 15 April to October 2025: 9,100 (300 in the 14 days before)
        new(D(2026, 4, 1), 140_000), new(D(2026, 11, 1), 149_100), new(D(2026, 11, 10), 149_400), // April to October 2026: 9,100
    ]);

    [Fact]
    public void TheEstimate_OfTheWorkedExample_CountsTheSetsMonthsTheFuelLogMeasured_LessTheDaysAnotherSetWasOn()
    {
        // A summer set in use since March 2023, the winter set on until 14 April 2025, the figure covering up to 10 November 2026.
        var estimate = TyreCalculator.Estimate(Example, [(D(2024, 11, 5), D(2025, 4, 15))], SummerMonths, D(2023, 3, 25), D(2026, 11, 10), Today);

        Assert.Equal(24_100, estimate.Distance); // 5,900 + 9,100 + 9,100
        Assert.Equal(D(2024, 6, 1), estimate.From);
        Assert.Equal([new DayRange(D(2023, 4, 1), D(2023, 10, 31)), new DayRange(D(2024, 4, 1), D(2024, 5, 31))], estimate.NotCovered);
        Assert.Equal(14, estimate.LeftOutDays);
        Assert.True(estimate.Readings);
    }

    [Fact]
    public void TheEstimate_OfAWinterSet_CountsMonthsThatWrapTheYear()
    {
        var log = TyreCalculator.Sorted([new(D(2024, 11, 1), 100_000), new(D(2025, 4, 1), 104_000), new(D(2025, 11, 1), 113_000), new(D(2026, 4, 1), 118_050)]);

        var estimate = TyreCalculator.Estimate(log, [], WinterMonths, D(2024, 11, 1), D(2026, 11, 10), Today);

        Assert.Equal(9_100, estimate.Distance); // 4,000 + 5,050 (rounded to 100), and 0 for November 2026: after the latest reading
        Assert.Equal(D(2024, 11, 1), estimate.From);
        Assert.Empty(estimate.NotCovered);
    }

    [Fact]
    public void TheEstimate_CountsTheWholeMonthOfInUseSince_AndUpToTheDayItEnds()
    {
        var log = TyreCalculator.Sorted([new(D(2026, 6, 1), 10_000), new(D(2026, 6, 20), 11_900), new(D(2026, 7, 11), 14_000), new(D(2026, 8, 1), 16_000)]);

        var estimate = TyreCalculator.Estimate(log, [], SummerMonths, D(2026, 6, 20), D(2026, 7, 11), Today);

        Assert.Equal(4_000, estimate.Distance); // from 1 June (the month's first day), to 11 July, not to the month's end
        Assert.Equal(D(2026, 6, 1), estimate.From);
    }

    [Fact]
    public void TheEstimate_WithoutAReading_SaysSo_AndWithNothingInTheMonths_IsZeroWithWhatIsNotCovered()
    {
        var none = TyreCalculator.Estimate([], [], SummerMonths, D(2023, 3, 25), D(2026, 11, 10), Today);
        Assert.Equal((0L, (DateOnly?)null, false), (none.Distance, none.From, none.Readings));
        Assert.Empty(none.NotCovered);

        // The fuel log starts after the span: every month of the set's is before it.
        var late = TyreCalculator.Estimate([new(D(2026, 11, 1), 149_100)], [], SummerMonths, D(2026, 3, 1), D(2026, 11, 10), Today);
        Assert.Equal((0L, (DateOnly?)null, true), (late.Distance, late.From, late.Readings));
        Assert.Equal([new DayRange(D(2026, 4, 1), D(2026, 10, 31))], late.NotCovered);
    }

    [Fact]
    public void TheEstimate_LeavesOutEveryInstallation_TheSetsOwnAndOneStillOnTheCar()
    {
        // The set's own measured installation inside the span (its figure's day never moved), and another set on the car since October.
        var estimate = TyreCalculator.Estimate(Example, [(D(2026, 4, 1), D(2026, 7, 1)), (D(2026, 10, 1), null)], SummerMonths, D(2026, 4, 1), D(2026, 11, 10), Today);

        Assert.Equal(91 + 31, estimate.LeftOutDays); // April to June, and October (up to today)
        Assert.Equal(D(2026, 7, 1), estimate.From);
        Assert.Equal(3_900, estimate.Distance); // July to September, a straight line between 1 April and 1 November: 9,100 × 92 / 214
    }
}
