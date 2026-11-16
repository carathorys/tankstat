namespace Tankstat.Domain.Tyres;

/// <summary>
/// The months of the year a tyre set is usually on the car (#291): what the automatic estimate of a used set's distance counts. Twelve
/// bits, January the lowest; never empty. A set keeps months of its own only when they differ from its season's
/// (<see cref="DefaultFor"/>), so a set on its season's follows a change of season.
/// </summary>
public readonly record struct TyreMonths
{
    private const int All = (1 << 12) - 1;

    private TyreMonths(int mask) => Mask = mask;

    /// <summary>The months as bits: bit 0 is January, bit 11 December. What the database keeps.</summary>
    public int Mask { get; }

    public static TyreMonths WholeYear => new(All);

    /// <summary>The months from a list as the API takes it (1 = January), in any order, a month named twice counting once.</summary>
    public static TyreMonths Of(IEnumerable<int> months)
    {
        var mask = 0;
        foreach (var month in months)
        {
            if (month is < 1 or > 12) throw new DomainException("tyres.monthInvalid", "A month is a number from 1 to 12.", new { Value = month.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            mask |= 1 << (month - 1);
        }
        if (mask == 0) throw new DomainException("tyres.monthsRequired", "Pick at least one month.");
        return new TyreMonths(mask);
    }

    /// <summary>The months a stored mask stands for; bits beyond December are dropped, and none at all is the whole year (never empty).</summary>
    public static TyreMonths FromMask(int mask) => (mask & All) is var kept && kept != 0 ? new(kept) : WholeYear;

    /// <summary>A season's usual months: summer April to October, winter November to March, all-season the whole year.</summary>
    public static TyreMonths DefaultFor(TyreSeason season) => season switch
    {
        TyreSeason.Summer => Of([4, 5, 6, 7, 8, 9, 10]),
        TyreSeason.Winter => Of([11, 12, 1, 2, 3]),
        _ => WholeYear,
    };

    /// <param name="month">1 = January.</param>
    public bool Has(int month) => month is >= 1 and <= 12 && (Mask & (1 << (month - 1))) != 0;

    /// <summary>The months in calendar order, 1 = January.</summary>
    public IReadOnlyList<int> ToList() => [.. Enumerable.Range(1, 12).Where(Has)];
}
