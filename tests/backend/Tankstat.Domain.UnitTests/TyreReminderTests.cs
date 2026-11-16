using Tankstat.Domain.Tyres;

namespace Tankstat.Domain.UnitTests;

/// <summary>
/// The swap reminder's rule (#292, the owner's decisions 72 to 80): a stored set is due from the 1st of its month until it goes on the car
/// or the season is dismissed; the handoff's truth table.
/// </summary>
public class TyreReminderTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static readonly DateOnly Nov16 = D(2026, 11, 16), Mar20 = D(2027, 3, 20), Nov5 = D(2027, 11, 5);
    private static readonly DateOnly Season26 = D(2026, 11, 1), Season27 = D(2027, 11, 1);

    /// <summary>A winter set whose reminder was set in September 2026.</summary>
    private static TyreSet Winter(int? month = null, DateOnly? setOn = null, TyreSeason season = TyreSeason.Winter) =>
        TyreSet.Create(Owner, Owner, Guid.NewGuid(), "Continental", season, null, D(2022, 11, 1), null, null, 0, null, reminderMonth: month, today: setOn ?? D(2026, 9, 10));

    private static DateOnly? Due(TyreSet set, DateOnly today, (DateOnly, DateOnly?)[]? own = null, params TyreSeason[] onTheCar) =>
        TyreReminder.DueSeason(set, own ?? [], onTheCar, today);

    [Fact]
    public void TheSeasonOfADay_IsTheLatestFirstOfItsMonth_AcrossNewYear()
    {
        Assert.Equal(Season26, TyreReminder.SeasonOf(11, D(2026, 11, 1)));
        Assert.Equal(Season26, TyreReminder.SeasonOf(11, D(2027, 3, 20)));
        Assert.Equal(D(2025, 11, 1), TyreReminder.SeasonOf(11, D(2026, 10, 31)));
        Assert.Equal(D(2027, 3, 1), TyreReminder.SeasonOf(3, D(2027, 3, 1)));
    }

    [Fact]
    public void ASetNeverFitted_IsDueFromItsMonth_AndAgainForTheNextSeason()
    {
        var set = Winter();
        Assert.Null(Due(set, D(2026, 10, 31))); // before its month: last November was before the reminder was set
        Assert.Equal(Season26, Due(set, Nov16));
        Assert.Equal(Season26, Due(set, Mar20));
        Assert.Equal(Season27, Due(set, Nov5)); // a new season: a second notification
    }

    [Fact]
    public void TheSwap_ClearsTheSeason_AlsoOnceTheSetIsOffAgain_AndTheNextSeasonIsDueUntilItsSwap()
    {
        var set = Winter();
        (DateOnly, DateOnly?)[] first = [(D(2026, 11, 12), D(2027, 3, 20))];
        Assert.Equal(Season26, Due(set, D(2026, 11, 11), first)); // a swap logged late but dated the 12th: due until that day
        Assert.Null(Due(set, Nov16, [(D(2026, 11, 12), null)]));
        Assert.Null(Due(set, Mar20, first));
        Assert.Equal(Season27, Due(set, Nov5, first));
        Assert.Null(Due(set, D(2027, 11, 10), [.. first, (D(2027, 11, 10), null)]));
    }

    [Fact]
    public void ASetFittedEarly_WasOnTheCarSinceTheSeasonBegan_SoItIsNotDueWhenItComesOff()
    {
        var set = Winter();
        (DateOnly, DateOnly?)[] early = [(D(2026, 10, 25), D(2027, 3, 20))];
        Assert.Null(Due(set, Nov16, early));
        Assert.Null(Due(set, Mar20, early));
        Assert.Equal(Season27, Due(set, Nov5, early));
        Assert.Equal(Season26, Due(set, Nov16, [(D(2026, 10, 1), D(2026, 11, 1))])); // off on the season's first day: not on the car since
    }

    [Fact]
    public void NotThisSeason_QuietsThatSeasonOnly_AndOnlyTheSeasonOfToday_CanBeDismissed()
    {
        var set = Winter();
        Assert.False(set.DismissReminder(D(2025, 11, 1), D(2026, 12, 10))); // an older season, sent late: nothing changes
        Assert.True(set.DismissReminder(Season26, D(2026, 12, 10)));
        Assert.False(set.DismissReminder(Season26, D(2026, 12, 11))); // again: nothing
        Assert.Null(Due(set, Mar20));
        Assert.Equal(Season27, Due(set, Nov5));
        Assert.True(set.RestoreReminder());
        Assert.False(set.RestoreReminder());
        Assert.Equal(Season26, Due(set, Mar20));
    }

    [Fact]
    public void ASetOfTheSameSeasonOnTheCar_SilencesIt_AnAllSeasonSetDoesNot()
    {
        var set = Winter();
        Assert.Null(Due(set, Nov16, null, TyreSeason.Winter));
        Assert.Equal(Season26, Due(set, Nov16, null, TyreSeason.AllSeason));
        Assert.Equal(Season26, Due(set, Nov16, null, TyreSeason.Summer));
    }

    [Fact]
    public void ASeasonCounts_OnlyFromTheMonthTheReminderWasSetIn()
    {
        Assert.Null(Due(Winter(setOn: D(2027, 6, 15)), D(2027, 6, 15))); // added to storage in June: last November does not count
        Assert.Equal(Season27, Due(Winter(setOn: D(2027, 6, 15)), Nov5));
        Assert.Equal(Season26, Due(Winter(setOn: D(2026, 11, 5)), D(2026, 11, 5))); // added while its month runs: at once
        Assert.Equal(Season26, Due(Winter(setOn: D(2026, 11, 30)), D(2026, 12, 1)));
        Assert.Null(Due(Winter(setOn: D(2026, 12, 1)), D(2026, 12, 1)));
    }

    [Fact]
    public void AMonthChanged_CountsFromTheDayItChanged_AndItsSeasonIsNotTheDismissedOne()
    {
        var set = Winter();
        set.DismissReminder(Season26, Nov16);
        set.Update("Continental", TyreSeason.Winter, null, D(2022, 11, 1), null, null, reminderMonth: 10, today: D(2026, 11, 20));

        Assert.Equal((10, D(2026, 11, 20)), (set.ReminderMonth!.Value, set.ReminderSince!.Value));
        Assert.Null(Due(set, D(2026, 11, 21))); // October last came before the change
        Assert.Equal(D(2027, 10, 1), Due(set, D(2027, 10, 2)));
    }

    [Fact]
    public void TheMonth_IsItsSeasons_UnlessChanged_FollowsAChangeOfSeason_AndZeroIsNoReminder()
    {
        Assert.Equal((null, 11), (Winter().ReminderMonth, Winter().EffectiveReminderMonth!.Value));
        Assert.Null(Winter(month: 11).ReminderMonth); // its season's: none of its own
        Assert.Equal((0, (int?)null), (Winter(month: 0).ReminderMonth!.Value, Winter(month: 0).EffectiveReminderMonth));
        Assert.Null(Due(Winter(month: 0), Nov16));
        Assert.Null(Winter(season: TyreSeason.AllSeason).EffectiveReminderMonth);
        Assert.Null(Winter(month: 0, season: TyreSeason.AllSeason).ReminderMonth); // an all-season set's default is none
        Assert.Equal("tyres.reminderMonthInvalid", Assert.Throws<DomainException>(() => Winter(month: 13)).Key);

        var follows = Winter();
        follows.Update("Continental", TyreSeason.Summer, null, D(2022, 11, 1), null, null, today: D(2026, 12, 2)); // a client from before the month
        Assert.Equal((3, D(2026, 12, 2)), (follows.EffectiveReminderMonth!.Value, follows.ReminderSince!.Value));
        var own = Winter(month: 10);
        own.Update("Continental", TyreSeason.Summer, null, D(2022, 11, 1), null, null, today: D(2026, 12, 2));
        Assert.Equal((10, D(2026, 9, 10)), (own.EffectiveReminderMonth!.Value, own.ReminderSince!.Value)); // unchanged: it counts as before
    }

    [Fact]
    public void ARetiredSet_AndOneInTheTrash_AreQuiet()
    {
        var retired = Winter();
        retired.Retire(D(2026, 10, 1));
        Assert.Null(Due(retired, Nov16));
        var trashed = Winter();
        trashed.MarkDeleted(DateTimeOffset.UtcNow);
        Assert.Null(Due(trashed, Nov16));
    }
}
