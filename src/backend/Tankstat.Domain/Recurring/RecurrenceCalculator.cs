namespace Tankstat.Domain.Recurring;

public enum RecurrenceState
{
    /// <summary>Not due for a while.</summary>
    Upcoming,

    /// <summary>Within the warning time or distance (or exactly due).</summary>
    DueSoon,

    /// <summary>The date or the odometer has passed.</summary>
    Overdue,
}

/// <summary>Which of the two limits decides the state: the one that is closer to (or already past) its due point.</summary>
public enum RecurrenceLimit
{
    Time,
    Odometer,
}

/// <summary>
/// Where a recurring expense stands. <see cref="DaysLeft"/> and <see cref="DistanceLeft"/> are negative once overdue and null when that
/// side does not apply (or the current odometer is unknown).
/// </summary>
public sealed record RecurrenceStatus(
    RecurrenceState State, RecurrenceLimit? Limit, DateOnly? DueDate, long? DueOdometer, int? DaysLeft, long? DistanceLeft);

/// <summary>Works out when a <see cref="RecurringExpense"/> is due and whether it already is. Pure: the caller supplies today and the odometer.</summary>
public static class RecurrenceCalculator
{
    /// <param name="currentOdometer">The latest known odometer reading of the vehicle, or null when there is none.</param>
    public static RecurrenceStatus Evaluate(RecurringExpense item, DateOnly today, long? currentOdometer)
    {
        DateOnly? dueDate = item is { UsesTime: true, IntervalMonths: { } months } ? item.LastDoneDate.AddMonths(months) : null;
        long? dueOdometer = item is { UsesDistance: true, IntervalDistance: { } distance, LastDoneOdometer: { } last } ? last + distance : null;
        int? daysLeft = dueDate is { } d ? d.DayNumber - today.DayNumber : null;
        long? distanceLeft = dueOdometer is { } o && currentOdometer is { } now ? o - now : null;

        var time = daysLeft is { } dl ? (StateOf(dl < 0, dl <= item.WarnDays), Share(dl, item.IntervalMonths!.Value * 30.4375)) : ((RecurrenceState, double)?)null;
        var odometer = distanceLeft is { } xl ? (StateOf(xl < 0, xl <= item.WarnDistance), Share(xl, item.IntervalDistance!.Value)) : ((RecurrenceState, double)?)null;

        // Whichever is further along wins; on a tie the one with the smaller share of its interval left.
        var (state, limit) = (time, odometer) switch
        {
            (null, null) => (RecurrenceState.Upcoming, (RecurrenceLimit?)null),
            (not null, null) => (time!.Value.Item1, RecurrenceLimit.Time),
            (null, not null) => (odometer!.Value.Item1, RecurrenceLimit.Odometer),
            _ => time!.Value.Item1 > odometer!.Value.Item1 || (time.Value.Item1 == odometer.Value.Item1 && time.Value.Item2 <= odometer.Value.Item2)
                ? (time.Value.Item1, RecurrenceLimit.Time)
                : (odometer.Value.Item1, RecurrenceLimit.Odometer),
        };
        return new RecurrenceStatus(state, limit, dueDate, dueOdometer, daysLeft, distanceLeft);
    }

    private static RecurrenceState StateOf(bool past, bool soon) => past ? RecurrenceState.Overdue : soon ? RecurrenceState.DueSoon : RecurrenceState.Upcoming;

    private static double Share(double left, double interval) => left / interval;
}
