using Tankstat.Domain.Tyres;

namespace Tankstat.Domain.UnitTests;

/// <summary>The months a tyre set is usually on the car (#291), and how a set keeps them: its own only when they differ from its season's.</summary>
public class TyreMonthsTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static TyreSet Set(TyreSeason season, IReadOnlyList<int>? months = null) =>
        TyreSet.Create(Owner, Owner, Guid.NewGuid(), "Michelin", season, null, new DateOnly(2023, 3, 25), null, null, 0, null, usualMonths: months);

    [Fact]
    public void ASeasonsUsualMonths_SummerAprilToOctober_WinterNovemberToMarch_AllSeasonTheWholeYear()
    {
        Assert.Equal([4, 5, 6, 7, 8, 9, 10], TyreMonths.DefaultFor(TyreSeason.Summer).ToList());
        Assert.Equal([1, 2, 3, 11, 12], TyreMonths.DefaultFor(TyreSeason.Winter).ToList());
        Assert.Equal(Enumerable.Range(1, 12), TyreMonths.DefaultFor(TyreSeason.AllSeason).ToList());
        Assert.Equal(TyreMonths.WholeYear, TyreMonths.DefaultFor(TyreSeason.AllSeason));
    }

    [Fact]
    public void AList_InAnyOrder_AMonthTwiceCountingOnce_ComesBackInCalendarOrder_AndThroughItsMask()
    {
        var months = TyreMonths.Of([10, 3, 4, 3]);

        Assert.Equal([3, 4, 10], months.ToList());
        Assert.True(months.Has(3));
        Assert.False(months.Has(5));
        Assert.False(months.Has(13));
        Assert.Equal(months, TyreMonths.FromMask(months.Mask));
        Assert.Equal(TyreMonths.WholeYear, TyreMonths.FromMask(0)); // never empty, whatever a database holds
        Assert.Equal(TyreMonths.Of([1]), TyreMonths.FromMask(1 | 1 << 12)); // bits beyond December dropped
    }

    [Fact]
    public void NoMonth_AndAMonthThatIsNone_AreRefused()
    {
        Assert.Equal("tyres.monthsRequired", Assert.Throws<DomainException>(() => TyreMonths.Of([])).Key);
        Assert.Equal("tyres.monthInvalid", Assert.Throws<DomainException>(() => TyreMonths.Of([0])).Key);
        Assert.Equal("tyres.monthInvalid", Assert.Throws<DomainException>(() => TyreMonths.Of([4, 13])).Key);
    }

    [Fact]
    public void ASet_KeepsMonthsOfItsOwn_OnlyWhenTheyDifferFromItsSeasons()
    {
        Assert.Null(Set(TyreSeason.Summer).UsualMonths);
        Assert.Null(Set(TyreSeason.Summer, [10, 9, 8, 7, 6, 5, 4]).UsualMonths); // its season's, in any order: kept as none
        var own = Set(TyreSeason.Summer, [3, 4, 5, 6, 7, 8, 9, 10]);
        Assert.Equal([3, 4, 5, 6, 7, 8, 9, 10], own.UsualMonths!.Value.ToList());
        Assert.Equal(own.UsualMonths, own.Months);
        Assert.Equal(TyreMonths.DefaultFor(TyreSeason.Winter), Set(TyreSeason.Winter).Months);
    }

    [Fact]
    public void AChangeOfSeason_MovesASetOnItsSeasonsMonths_AndKeepsMonthsOfItsOwn()
    {
        var onDefault = Set(TyreSeason.Summer);
        var own = Set(TyreSeason.Summer, [5, 6, 7]);

        onDefault.Update("Michelin", TyreSeason.Winter, null, onDefault.Since, null, null); // a client from before the months sends none
        own.Update("Michelin", TyreSeason.Winter, null, own.Since, null, null);

        Assert.Equal(TyreMonths.DefaultFor(TyreSeason.Winter), onDefault.Months);
        Assert.Equal([5, 6, 7], own.Months.ToList());
    }

    [Fact]
    public void AnEdit_AndTheRoughFigure_TakeMonths_NormalisedAgainstTheSeasonTheSetHasThen()
    {
        var set = Set(TyreSeason.Summer, [5, 6, 7]);

        set.Update("Michelin", TyreSeason.Winter, null, set.Since, null, null, usualMonths: [11, 12, 1, 2, 3]); // the new season's: none of its own
        Assert.Null(set.UsualMonths);

        set.SetRough(24_000, new DateOnly(2026, 11, 10), usualMonths: [11, 12, 1, 2]);
        Assert.Equal([1, 2, 11, 12], set.UsualMonths!.Value.ToList());
        set.SetRough(25_000, null); // left out: kept
        Assert.Equal([1, 2, 11, 12], set.UsualMonths!.Value.ToList());
        Assert.Equal("tyres.monthsRequired", Assert.Throws<DomainException>(() => set.SetRough(25_000, null, usualMonths: [])).Key);
    }
}
