namespace Tankstat.Domain.Tyres;

/// <summary>
/// The swap reminder (#292, the owner's decisions 72 to 80 on the issue), pure and worked out on every read: a set in storage is due from
/// the 1st of its month, until it goes on the car or <em>Not this season</em> is tapped. Nothing but the month, the day the reminder was
/// set and the dismissed season is stored.
/// </summary>
public static class TyreReminder
{
    /// <summary>A season's usual month to fit its tyres: winter November, summer March, all-season none.</summary>
    public static int? DefaultMonth(TyreSeason season) => season switch
    {
        TyreSeason.Winter => 11,
        TyreSeason.Summer => 3,
        _ => null,
    };

    /// <summary>The season of a month on a day: the latest 1st of that month at or before the day.</summary>
    public static DateOnly SeasonOf(int month, DateOnly today) => new(month <= today.Month ? today.Year : today.Year - 1, month, 1);

    /// <summary>
    /// The season a set is due for on <paramref name="today"/>, or null while it is quiet. Due: it is live and not retired, has a month,
    /// its season began no earlier than the month the reminder was set in (decision 78), it has not been on the car at any time since the
    /// season began (the swap clears it; one fitted early and taken off later stays cleared), no set of its own season is on the car now
    /// (decision 74; an all-season set on the car silences only all-season sets), and the season was not dismissed (decisions 73, 77).
    /// </summary>
    /// <param name="own">The set's live installations.</param>
    /// <param name="onTheCar">The seasons of the other live sets on the car today.</param>
    public static DateOnly? DueSeason(TyreSet set, IEnumerable<(DateOnly From, DateOnly? Until)> own, IEnumerable<TyreSeason> onTheCar, DateOnly today)
    {
        if (set.IsDeleted || set.IsRetired || set.EffectiveReminderMonth is not { } month) return null;
        var season = SeasonOf(month, today);
        if (set.ReminderSince is { } since && season < new DateOnly(since.Year, since.Month, 1)) return null;
        if (own.Any(i => i.From <= today && (i.Until is not { } until || until > season))) return null;
        if (onTheCar.Contains(set.Season)) return null;
        return set.ReminderDismissedFor == season ? null : season;
    }
}
