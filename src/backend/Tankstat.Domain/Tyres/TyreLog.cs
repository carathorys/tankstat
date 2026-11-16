namespace Tankstat.Domain.Tyres;

/// <summary>The tyre log's lines a change made or changed (the entities are changed in place; the caller saves them).</summary>
public sealed class TyreLogChanges
{
    public List<TyreEntry> Created { get; } = [];
    public List<TyreEntry> Changed { get; } = [];

    public void Change(TyreEntry entry)
    {
        if (!Created.Contains(entry) && !Changed.Contains(entry)) Changed.Add(entry);
    }
}

/// <summary>
/// How the tyre log follows the history (#288): every installation has its line, which takes the installation's id. A change of the history
/// gives each installation it creates a line (<see cref="TyreEntryKind.PositionsChanged"/> when the same set came off that day, else
/// <see cref="TyreEntryKind.Swap"/>), trashes the line of each installation it trashes, and moves the line of each one whose first day moved.
/// An installation moved to another set (#332) takes its line along, and the kinds around it are worked out again.
/// An installation a join took away from its first day leaves what its line held for that day (a reading, a note) on that day, on a
/// <see cref="TyreEntryKind.Note"/> line of the set. Pure; the device ports it (<c>src/tyres/tyreLog.ts</c>), pinned by the swap contract.
/// </summary>
public static class TyreLog
{
    /// <param name="history">The history after the change (its live installations name the kind of a new line).</param>
    /// <param name="entries">The vehicle's lines as they are (the trash included).</param>
    public static TyreLogChanges Follow(
        TyreChanges changes, TyreHistory history, IReadOnlyCollection<TyreEntry> entries, Guid by, DateTimeOffset now, TyreLogChanges? into = null)
    {
        var result = into ?? new TyreLogChanges();
        TyreEntry? LineOf(TyreInstallation installation) =>
            result.Created.FirstOrDefault(e => e.Id == installation.Id) ?? entries.FirstOrDefault(e => e.Id == installation.Id);
        foreach (var (installation, _) in changes.JoinedAway)
            if (LineOf(installation) is { IsDeleted: false } line && (line.OdometerReading is not null || line.Note is not null))
            {
                result.Created.Add(TyreEntry.Keeping(line, by));
                result.Change(line);
            }
        foreach (var installation in changes.Trashed)
            if (LineOf(installation) is { IsDeleted: false } line)
            {
                line.MarkDeleted(now, by);
                result.Change(line);
            }
        foreach (var installation in changes.Changed)
            if (LineOf(installation) is { IsDeleted: false } line && line.MoveTo(installation.From, by)) result.Change(line);
        foreach (var installation in changes.Created) result.Created.Add(LineFor(installation, history, by));
        // A period moved to another set (#332): its line takes the set, and its kind and the kind of the line that starts where it ends are
        // worked out again, as a new line's would be (a swap, or positions changed when the same set came off that day).
        foreach (var installation in changes.SetChanged)
        {
            var next = history.Live.Where(o => o != installation && installation.Until is { } end && o.From == end);
            foreach (var affected in next.Prepend(installation))
                if (LineOf(affected) is { IsDeleted: false } line && line.FollowInstallation(affected.SetId, PositionsChanged(affected, history), by)) result.Change(line);
        }
        return result;
    }

    private static bool PositionsChanged(TyreInstallation installation, TyreHistory history) =>
        history.Live.Any(o => o != installation && o.SetId == installation.SetId && o.Until == installation.From);

    /// <summary>The line of a new installation: a change of positions when the same set came off on its first day.</summary>
    public static TyreEntry LineFor(TyreInstallation installation, TyreHistory history, Guid by) =>
        TyreEntry.ForInstallation(installation, by, positionsChanged: PositionsChanged(installation, history));
}
