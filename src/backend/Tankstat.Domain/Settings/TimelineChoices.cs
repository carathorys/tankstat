namespace Tankstat.Domain.Settings;

/// <summary>How far back the tyre timeline reaches (#332).</summary>
public enum TimelineRange
{
    All,
    TwoYears,
    TwelveMonths,
}

/// <summary>What the tyre timeline draws besides the periods on the car (#332), each shown or not.</summary>
public enum TimelineLayer
{
    /// <summary>The time before tracking, which a set's rough figure stands for.</summary>
    BeforeTracking,
    Gaps,
    /// <summary>Repairs and costs: the tyre log's lines that are no swap.</summary>
    Marks,
    RetiredSets,
    Table,
}

/// <summary>
/// What a user chose to see on the tyre timeline (#332, the owner's decisions 86 to 88), kept with the account: the range, the layers shown
/// and the sets hidden. Shape and limits only: whether a set id still names a set is the UI's business (it drops the ones it no longer
/// knows). A user who never chose has none, and the defaults apply: the whole range, everything but the retired sets, no set hidden.
/// </summary>
public sealed record TimelineChoices
{
    /// <summary>The most set ids kept; with more, the oldest (the first ones) are dropped.</summary>
    public const int MaxHiddenSets = 50;

    public TimelineRange Range { get; }
    public IReadOnlyList<TimelineLayer> Layers { get; }
    public IReadOnlyList<Guid> HiddenSets { get; }

    [System.Text.Json.Serialization.JsonConstructor]
    public TimelineChoices(TimelineRange range, IReadOnlyList<TimelineLayer>? layers, IReadOnlyList<Guid>? hiddenSets)
    {
        if (!Enum.IsDefined(range)) throw new DomainException("settings.timelineInvalid", "The timeline's range is not one the app offers.");
        var shown = (layers ?? []).Distinct().Order().ToList();
        if (shown.Any(l => !Enum.IsDefined(l))) throw new DomainException("settings.timelineInvalid", "The timeline's layers are not ones the app offers.");
        var hidden = (hiddenSets ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        Range = range;
        Layers = shown;
        HiddenSets = hidden.Count > MaxHiddenSets ? hidden[^MaxHiddenSets..] : hidden;
    }

    public bool Equals(TimelineChoices? other) =>
        other is not null && Range == other.Range && Layers.SequenceEqual(other.Layers) && HiddenSets.SequenceEqual(other.HiddenSets);

    public override int GetHashCode() => HashCode.Combine(Range, Layers.Count, HiddenSets.Count);
}
