using System.Reflection;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;

namespace Tankstat.Application.Imports;

/// <summary>What an entry of the backup is to the target car.</summary>
public enum BackupEntryMatch
{
    /// <summary>The target has no such entry: it can come in.</summary>
    OnlyInBackup,
    /// <summary>The target has it, with other values in <see cref="BackupEntryComparison{T}.Differences"/>.</summary>
    Differs,
    /// <summary>The target has it as it is in the backup: nothing to do.</summary>
    Same,
}

/// <summary>A value that differs between the backup's entry and the target's, by the input's field name (camelCase), each side as it is.</summary>
public sealed record BackupFieldDifference(string Field, object? Backup, object? Target);

/// <summary>An entry of the backup against the target car: the target's entry it was matched with (by id, else by key), and what differs.</summary>
public sealed record BackupEntryComparison<T>(T Entry, BackupEntryMatch Match, Guid? TargetId, bool MatchedById, IReadOnlyList<BackupFieldDifference> Differences);

/// <summary>One kind of a car's entries compared, in the backup's order; <see cref="Page"/> hands them out a screenful at a time.</summary>
public sealed record BackupKindComparison<T>(IReadOnlyList<BackupEntryComparison<T>> Entries)
{
    public int OnlyInBackup => Entries.Count(e => e.Match == BackupEntryMatch.OnlyInBackup);
    public int Differs => Entries.Count(e => e.Match == BackupEntryMatch.Differs);
    public int Same => Entries.Count(e => e.Match == BackupEntryMatch.Same);

    public IReadOnlyList<BackupEntryComparison<T>> Page(int skip, int take, BackupEntryMatch? only = null) =>
        [.. Entries.Where(e => only is null || e.Match == only).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 200))];
}

/// <summary>A backup's car against a car that is here, kind by kind.</summary>
public sealed record BackupCarComparison(
    BackupKindComparison<BackupRefueling> Refuelings, BackupKindComparison<BackupExpense> Expenses, BackupKindComparison<BackupSchedule> Schedules);

/// <summary>
/// Compares a backup's car with a target car (#336; the owner's rules in #334): an entry is the target's by its <b>id</b> first, else by
/// its <b>key</b>, as the Fuelio import finds duplicates (a refuelling by date and odometer, an expense by date, title and amount, a
/// schedule by title). Only the target car's own entries are looked at, so an id that exists in someone else's car tells nothing: it is
/// "only in the backup". When several entries of the backup share a key with one of the target's, the first (by date, then id) takes it
/// and the others are only in the backup. "Differs" is about what an edit could change (the values of the service's input), never what
/// is worked out (consumption, versions, who changed it when).
/// </summary>
public static class BackupCarComparer
{
    public static BackupCarComparison Compare(BackupCar backup, BackupCar target) => new(
        Kind(backup.Refuelings, target.Refuelings, r => r.Id, r => r.Values, r => (r.Values.Date, r.Values.Odometer), r => r.Values.Date),
        Kind(backup.Expenses, target.Expenses, e => e.Id, e => e.Values, e => (e.Values.Date, e.Values.Title.Trim().ToLowerInvariant(), e.Values.Amount), e => e.Values.Date),
        Kind(backup.Schedules, target.Schedules, s => s.Id, s => s.Values, s => s.Values.Title.Trim().ToLowerInvariant(), _ => default));

    private static BackupKindComparison<T> Kind<T, TValues, TKey>(
        IReadOnlyList<T> backup, IReadOnlyList<T> target, Func<T, Guid> id, Func<T, TValues> values, Func<T, TKey> key, Func<T, DateOnly> date)
        where TKey : notnull where TValues : notnull
    {
        var byId = target.GroupBy(id).ToDictionary(g => g.Key, g => g.First());
        var taken = new HashSet<Guid>();
        var matched = new Dictionary<Guid, (T Target, bool ById)>();
        // By id first, for every entry: a key must never take the target's entry that another entry of the backup is by its id.
        foreach (var entry in backup)
            if (byId.TryGetValue(id(entry), out var same) && taken.Add(id(same))) matched[id(entry)] = (same, true);
        var byKey = target.Where(t => !taken.Contains(id(t))).GroupBy(key).ToDictionary(g => g.Key, g => new Queue<T>(g.OrderBy(date).ThenBy(id)));
        foreach (var entry in backup.Where(e => !matched.ContainsKey(id(e))).OrderBy(date).ThenBy(id))
            if (byKey.TryGetValue(key(entry), out var free) && free.Count > 0) matched[id(entry)] = (free.Dequeue(), false);

        return new BackupKindComparison<T>([.. backup.Select(entry =>
        {
            if (!matched.TryGetValue(id(entry), out var m)) return new BackupEntryComparison<T>(entry, BackupEntryMatch.OnlyInBackup, null, false, []);
            var differences = Differences(values(entry), values(m.Target));
            return new BackupEntryComparison<T>(entry, differences.Count == 0 ? BackupEntryMatch.Same : BackupEntryMatch.Differs, id(m.Target), m.ById, differences);
        })]);
    }

    /// <summary>The input's fields whose values differ, in the input's own order. An empty text is no text; a number is its value (40 is 40.0).</summary>
    private static IReadOnlyList<BackupFieldDifference> Differences<TValues>(TValues backup, TValues target) where TValues : notnull =>
        [.. Properties<TValues>.All
            .Select(p => (Field: char.ToLowerInvariant(p.Name[0]) + p.Name[1..], Backup: Normal(p.GetValue(backup)), Target: Normal(p.GetValue(target))))
            .Where(f => !Equals(f.Backup, f.Target))
            .Select(f => new BackupFieldDifference(f.Field, f.Backup, f.Target))];

    private static object? Normal(object? value) => value switch
    {
        string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
        decimal number => number / 1.000000000000000000000000000000m, // 40.0 and 40 the same, also as text
        _ => value,
    };

    private static class Properties<TValues>
    {
        public static readonly PropertyInfo[] All = typeof(TValues).GetProperties(BindingFlags.Public | BindingFlags.Instance);
    }
}
