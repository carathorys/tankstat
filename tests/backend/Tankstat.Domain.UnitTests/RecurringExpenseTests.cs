using Tankstat.Domain;
using Tankstat.Domain.Recurring;

namespace Tankstat.Domain.UnitTests;

public class RecurringExpenseTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Car = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 1, 15);

    private static RecurringExpense Make(
        RecurrenceKind kind = RecurrenceKind.Combined, int? months = 12, long? distance = 15000, long? odometer = 50000, int warnDays = 30, long warnDistance = 500,
        string title = "Oil change") =>
        RecurringExpense.Create(Owner, Owner, Car, title, "Service", null, kind, months, distance, Start, odometer, warnDays, warnDistance, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    private static string Key(Action action) => Assert.Throws<DomainException>(action).Key;

    [Fact]
    public void Create_TrimsText_AndKeepsOnlyWhatTheKindUses()
    {
        var time = Make(RecurrenceKind.Time, 12, 15000, 50000, title: "  Insurance ");
        var distance = Make(RecurrenceKind.Odometer, 12, 15000, 50000);

        Assert.Equal(("Insurance", 12, (long?)null), (time.Title, time.IntervalMonths, time.IntervalDistance));
        Assert.Equal(((int?)null, 15000L), (distance.IntervalMonths, distance.IntervalDistance));
        Assert.True(Make().UsesTime && Make().UsesDistance);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), Make().CreatedAt); // when it was added is kept
    }

    [Theory]
    [InlineData(RecurrenceKind.Time, null, null, null, "recurring.monthsInvalid")]
    [InlineData(RecurrenceKind.Time, 0, null, null, "recurring.monthsInvalid")]
    [InlineData(RecurrenceKind.Time, 601, null, null, "recurring.monthsInvalid")]
    [InlineData(RecurrenceKind.Odometer, null, null, 100L, "recurring.distanceInvalid")]
    [InlineData(RecurrenceKind.Odometer, null, 1000L, null, "recurring.odometerRequired")]
    [InlineData(RecurrenceKind.Combined, 12, 1000L, null, "recurring.odometerRequired")]
    [InlineData(RecurrenceKind.Combined, null, 1000L, 100L, "recurring.monthsInvalid")]
    [InlineData(RecurrenceKind.Time, 12, null, -5L, "odometer.negative")]
    [InlineData((RecurrenceKind)9, 12, 1000L, 100L, "recurring.unknownKind")]
    public void Create_RejectsAnIncompleteOrInvalidSchedule(RecurrenceKind kind, int? months, long? distance, long? odometer, string key) =>
        Assert.Equal(key, Key(() => Make(kind, months, distance, odometer)));

    [Fact]
    public void Create_ChecksTextAndWarningLimits()
    {
        Assert.Equal("recurring.titleRequired", Key(() => Make(title: " ")));
        Assert.Equal("recurring.titleTooLong", Key(() => Make(title: new string('x', 121))));
        Assert.Equal("recurring.warnDaysInvalid", Key(() => Make(warnDays: -1)));
        Assert.Equal("recurring.warnDaysInvalid", Key(() => Make(warnDays: 366)));
        Assert.Equal("recurring.warnDistanceInvalid", Key(() => Make(warnDistance: -1)));
        Assert.Equal("recurring.titleRequired", Key(() => Make(title: null!)));
        Assert.Equal("recurring.categoryTooLong", Key(() => Make().Update("Oil", new string('c', RecurringExpense.MaxCategoryLength + 1), null, RecurrenceKind.Time, 12, null, Start, null, 30, 500)));
        Assert.Equal("recurring.noteTooLong", Key(() => Make().Update("Oil", null, new string('n', RecurringExpense.MaxNoteLength + 1), RecurrenceKind.Time, 12, null, Start, null, 30, 500)));
    }

    [Fact]
    public void ATimeScheduleWithoutABaselineOdometer_TakesTheOneItIsDoneAt()
    {
        var item = Make(RecurrenceKind.Time, 12, null, null);

        item.MarkDone(new DateOnly(2026, 6, 1), 1000);
        Assert.Equal(1000L, item.LastDoneOdometer);

        item.MarkDone(new DateOnly(2026, 7, 1), 1000); // the same reading again is not going backwards
        Assert.Equal((new DateOnly(2026, 7, 1), 1000L), (item.LastDoneDate, item.LastDoneOdometer));
    }

    [Fact]
    public void MarkDone_StartsTheNextIntervalFromTheDayAndTheOdometer()
    {
        var item = Make();

        item.MarkDone(new DateOnly(2026, 6, 1), 58000);

        Assert.Equal((new DateOnly(2026, 6, 1), 58000L), (item.LastDoneDate, item.LastDoneOdometer));
    }

    [Fact]
    public void MarkDone_NeedsTheOdometerOnlyWhenTheKindUsesDistance_AndNeverGoesBackwards()
    {
        var timeOnly = Make(RecurrenceKind.Time, 12, null, null);
        timeOnly.MarkDone(new DateOnly(2026, 6, 1), null);
        Assert.Equal(new DateOnly(2026, 6, 1), timeOnly.LastDoneDate);

        var combined = Make();
        Assert.Equal("recurring.doneOdometerRequired", Key(() => combined.MarkDone(new DateOnly(2026, 6, 1), null)));
        Assert.Equal("recurring.odometerBelowLast", Key(() => combined.MarkDone(new DateOnly(2026, 6, 1), 49000)));
        Assert.Equal("recurring.doneBeforeLast", Key(() => combined.MarkDone(new DateOnly(2025, 12, 31), 51000)));
        Assert.Equal((Start, 50000L), (combined.LastDoneDate, combined.LastDoneOdometer)); // nothing changed by the failures
    }

    [Fact]
    public void ARefusedDone_NamesTheSchedule_SoTheDialogCanSayWhichOneOfSeveral()
    {
        var oil = Make(title: "Oil change");

        var refusals = new Action[]
        {
            () => oil.CheckDone(new DateOnly(2025, 12, 31), 51000),
            () => oil.CheckDone(new DateOnly(2026, 6, 1), null),
            () => oil.CheckDone(new DateOnly(2026, 6, 1), 49000),
        }.Select(Assert.Throws<DomainException>).ToList();

        Assert.All(refusals, e => Assert.Equal("Oil change", e.Args["title"]));
        Assert.Equal("2026-01-15", refusals[0].Args["date"]);
        Assert.Equal(50000L, refusals[2].Args["last"]);
    }

    [Fact]
    public void Update_ChangesTheDefinition_WithTheSameRules()
    {
        var item = Make();

        item.Update("Tyres", null, "note", RecurrenceKind.Odometer, null, 40000, Start, 50000, 10, 1000);

        Assert.Equal(("Tyres", RecurrenceKind.Odometer, (int?)null, (long?)40000), (item.Title, item.Kind, item.IntervalMonths, item.IntervalDistance));
        Assert.Equal("recurring.titleRequired", Key(() => item.Update("", null, null, RecurrenceKind.Odometer, null, 40000, Start, 50000, 10, 1000)));
    }
}

public class RecurrenceCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 1, 15);

    private static RecurringExpense Make(RecurrenceKind kind, int? months = 12, long? distance = 15000, long? odometer = 50000) =>
        RecurringExpense.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Oil", null, null, kind, months, distance, Start, odometer, 30, 500, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Time_DueDateIsTheIntervalAfterTheLastDone_AndTheStateFollowsTheWarning()
    {
        var item = Make(RecurrenceKind.Time, 12, null, null); // due 2027-01-15

        var far = RecurrenceCalculator.Evaluate(item, new DateOnly(2026, 6, 1), null);
        var soon = RecurrenceCalculator.Evaluate(item, new DateOnly(2026, 12, 20), null);
        var dueDay = RecurrenceCalculator.Evaluate(item, new DateOnly(2027, 1, 15), null);
        var late = RecurrenceCalculator.Evaluate(item, new DateOnly(2027, 1, 16), null);

        Assert.Equal(new DateOnly(2027, 1, 15), far.DueDate);
        Assert.Equal((RecurrenceState.Upcoming, RecurrenceLimit.Time), (far.State, far.Limit!.Value));
        Assert.Equal((RecurrenceState.DueSoon, 26), (soon.State, soon.DaysLeft));
        Assert.Equal((RecurrenceState.DueSoon, 0), (dueDay.State, dueDay.DaysLeft));
        Assert.Equal((RecurrenceState.Overdue, -1), (late.State, late.DaysLeft));
        Assert.Null(far.DueOdometer);
    }

    [Fact]
    public void Odometer_DueOdometerIsTheIntervalAfterTheLastDone_AndNeedsAKnownCurrentValue()
    {
        var item = Make(RecurrenceKind.Odometer, null, 15000, 50000); // due at 65000
        var today = new DateOnly(2026, 3, 1);

        Assert.Equal(RecurrenceState.Upcoming, RecurrenceCalculator.Evaluate(item, today, 55000).State);
        var soon = RecurrenceCalculator.Evaluate(item, today, 64600);
        Assert.Equal((RecurrenceState.DueSoon, 400L, 65000L), (soon.State, soon.DistanceLeft, soon.DueOdometer));
        var over = RecurrenceCalculator.Evaluate(item, today, 65001);
        Assert.Equal((RecurrenceState.Overdue, -1L), (over.State, over.DistanceLeft));
        var unknown = RecurrenceCalculator.Evaluate(item, today, null); // no reading yet: nothing to compare with
        Assert.Equal((RecurrenceState.Upcoming, (RecurrenceLimit?)null, (long?)null), (unknown.State, unknown.Limit, unknown.DistanceLeft));
    }

    [Fact]
    public void Combined_WhicheverIsReachedFirstDecides()
    {
        var item = Make(RecurrenceKind.Combined, 12, 15000, 50000); // 2027-01-15 or 65000

        var byTime = RecurrenceCalculator.Evaluate(item, new DateOnly(2027, 2, 1), 55000);
        var byDistance = RecurrenceCalculator.Evaluate(item, new DateOnly(2026, 3, 1), 66000);
        var neither = RecurrenceCalculator.Evaluate(item, new DateOnly(2026, 3, 1), 55000);

        Assert.Equal((RecurrenceState.Overdue, RecurrenceLimit.Time), (byTime.State, byTime.Limit!.Value));
        Assert.Equal((RecurrenceState.Overdue, RecurrenceLimit.Odometer), (byDistance.State, byDistance.Limit!.Value));
        Assert.Equal(RecurrenceState.Upcoming, neither.State);
        Assert.Equal(15000 - 5000, neither.DistanceLeft); // both limits are still reported
        Assert.NotNull(neither.DueDate);
    }

    [Fact]
    public void Combined_WithoutAKnownOdometer_FallsBackToTheCalendar()
    {
        var item = Make(RecurrenceKind.Combined, 12, 15000, 50000);

        var status = RecurrenceCalculator.Evaluate(item, new DateOnly(2027, 2, 1), null);

        Assert.Equal((RecurrenceState.Overdue, RecurrenceLimit.Time, (long?)null), (status.State, status.Limit!.Value, status.DistanceLeft));
    }

    [Fact]
    public void Combined_OnATie_TheLimitWithTheSmallerShareLeftIsReported()
    {
        var item = Make(RecurrenceKind.Combined, 12, 15000, 50000);

        // both are "due soon": 20 days of 12 months left (~5%) against 400 of 15000 (~2.7%)
        var status = RecurrenceCalculator.Evaluate(item, new DateOnly(2026, 12, 26), 64600);

        Assert.Equal((RecurrenceState.DueSoon, RecurrenceLimit.Odometer), (status.State, status.Limit!.Value));
    }
}

public class RecurringDoneDefaultsTests
{
    private static RecurringExpense Item(string title, string? category = null, string? note = null) =>
        RecurringExpense.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), title, category, note, RecurrenceKind.Time, 12, null, new DateOnly(2026, 1, 15), null, 30, 500, DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheTitle_JoinsTheSchedules_AndIsCutToFitAnExpenseTitle()
    {
        Assert.Equal("Oil change", RecurringDoneDefaults.Title([Item("Oil change")]));
        Assert.Equal("Oil change, Oil filter, Air filter", RecurringDoneDefaults.Title([Item("Oil change"), Item("Oil filter"), Item("Air filter")]));

        var long_ = RecurringDoneDefaults.Title([Item(new string('a', 70)), Item(new string('b', 70))]);
        Assert.Equal(Tankstat.Domain.Vehicles.Expense.MaxTitleLength, long_.Length);
        Assert.EndsWith("…", long_);
    }

    [Fact]
    public void TheCategory_IsTheCommonOne_OrTheFirstGiven_AndTheNoteGoesOnlyWithASingleSchedule()
    {
        Assert.Equal("Service", RecurringDoneDefaults.Category([Item("Oil", "Service"), Item("Filter", "Service")]));
        Assert.Equal("Service", RecurringDoneDefaults.Category([Item("Wipers"), Item("Oil", "Service"), Item("Tax", "Fees")]));
        Assert.Null(RecurringDoneDefaults.Category([Item("Wipers"), Item("Bulbs")]));

        Assert.Equal("5W-30", RecurringDoneDefaults.Note([Item("Oil", note: "5W-30")]));
        Assert.Null(RecurringDoneDefaults.Note([Item("Oil", note: "5W-30"), Item("Filter")]));
    }
}
