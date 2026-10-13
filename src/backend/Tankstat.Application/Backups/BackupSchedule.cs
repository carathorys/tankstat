using System.Globalization;

namespace Tankstat.Application.Backups;

/// <summary>
/// When a scheduled backup is due: on the chosen days of the week (<c>Backups:Days</c>, every day unless set), at each of one or more times
/// of the server's local time (<c>Backups:Every</c>, such as <c>02:00</c> or <c>02:00, 14:00</c>). On the night the clocks skip a time
/// (02:00 on the spring night in much of Europe) it is due at the first time that exists after it; on the night they repeat it, at the first
/// of the two. A start that finds the last due time passed without a backup since (the app was down, or this is its first start with a
/// schedule, or the folder holds no backup of the app's own since) has missed one, and writes it at once.
/// </summary>
public sealed class BackupSchedule
{
    /// <summary>The value of <c>Backups:Days</c> that chooses no day: nothing is scheduled (an empty value would read as unset, every day).</summary>
    public const string NoDay = "None";

    /// <summary>The days in the order they are written and offered: the week from Monday.</summary>
    public static readonly IReadOnlyList<DayOfWeek> Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary><c>Backups:Days</c> by default: the whole week.</summary>
    public static readonly string EveryDay = string.Join(",", Week);

    private static readonly char[] Separators = [',', ';', ' '];

    /// <summary>How a value is split: each part trimmed of any whitespace (a line break, a tab, a non-breaking space), empty ones left out.</summary>
    private const StringSplitOptions Parts = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

    private BackupSchedule(IReadOnlyList<TimeOnly> times, IReadOnlyList<DayOfWeek> days)
    {
        Times = times;
        Days = days;
    }

    /// <summary>The times of day, earliest first, each once.</summary>
    public IReadOnlyList<TimeOnly> Times { get; }

    /// <summary>The days of the week, from Monday, each once; never none (nothing is scheduled then).</summary>
    public IReadOnlyList<DayOfWeek> Days { get; }

    public bool IsEveryDay => Days.Count == Week.Count;

    /// <summary>The schedule as one text (<c>02:00,14:00 Monday,Friday</c>): two schedules are the same when theirs are.</summary>
    public override string ToString() =>
        $"{string.Join(",", Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)))} {string.Join(",", Days)}";

    /// <summary>
    /// The schedule <paramref name="every"/> and <paramref name="days"/> describe; null when nothing is scheduled (no time, or no day) or
    /// either is no value of its setting (the options' validator says so).
    /// </summary>
    public static BackupSchedule? Of(string? every, string? days) =>
        ParseTimes(every) is { Count: > 0 } times && ParseDays(days) is { Count: > 0 } chosen ? new BackupSchedule(times, chosen) : null;

    /// <summary>
    /// Times of day such as <c>02:00</c> or <c>2:30, 14:00</c> (<c>H</c> takes one digit or two; commas, semicolons or spaces between them),
    /// earliest first, each once; empty for nothing, null when any of them is no time. Whitespace around a time is no part of it: a value
    /// read from a file or a secret often ends in a line break (<c>02:00\n</c>), which must not stop the start.
    /// </summary>
    public static IReadOnlyList<TimeOnly>? ParseTimes(string? text)
    {
        var times = new SortedSet<TimeOnly>();
        foreach (var part in (text ?? "").Split(Separators, Parts))
        {
            if (!TimeOnly.TryParseExact(part, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return null;
            times.Add(at);
        }
        return [.. times];
    }

    /// <summary>
    /// Days of the week by their English names or first three letters, in any case and order (<c>Monday,Friday</c>, <c>mon fri</c>), from
    /// Monday, each once; the whole week for nothing (unset), none for <see cref="NoDay"/>, null when any of them is no day. Whitespace
    /// around a day is no part of it, as with the times.
    /// </summary>
    public static IReadOnlyList<DayOfWeek>? ParseDays(string? text)
    {
        var parts = (text ?? "").Split(Separators, Parts);
        if (parts.Length == 0) return Week;
        if (parts is [var only] && only.Equals(NoDay, StringComparison.OrdinalIgnoreCase)) return [];
        var days = new HashSet<DayOfWeek>();
        foreach (var part in parts)
        {
            var day = Week.Where(d => d.ToString().Equals(part, StringComparison.OrdinalIgnoreCase) || d.ToString()[..3].Equals(part, StringComparison.OrdinalIgnoreCase)).ToList();
            if (day.Count == 0) return null;
            days.Add(day[0]);
        }
        return [.. Week.Where(days.Contains)];
    }

    /// <summary>The first due time after <paramref name="now"/>.</summary>
    public DateTimeOffset NextAfter(DateTimeOffset now, TimeZoneInfo zone) => DuesAround(now, zone).Where(due => due > now).Min();

    /// <summary>The last due time at or before <paramref name="now"/>.</summary>
    public DateTimeOffset LastDueAtOrBefore(DateTimeOffset now, TimeZoneInfo zone) => DuesAround(now, zone).Where(due => due <= now).Max();

    /// <summary>
    /// Every due time of the days from a week before the local day of <paramref name="now"/> to a week after it (a chosen day comes in
    /// every week, so there is one on either side). They are compared as instants, never day by day: on a night the clocks jump at midnight,
    /// a time just after it can come before one just before it (with times on both sides of midnight).
    /// </summary>
    private IEnumerable<DateTimeOffset> DuesAround(DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = LocalDay(now, zone);
        for (var offset = -8; offset <= 8; offset++)
            foreach (var due in DuesOn(today.AddDays(offset), zone))
                yield return due;
    }

    /// <summary>Whether the last due time passed without a backup since: <paramref name="lastBackup"/> is the newest the app wrote (null: none).</summary>
    public bool Missed(DateTimeOffset? lastBackup, DateTimeOffset now, TimeZoneInfo zone) =>
        lastBackup is not { } last || last < LastDueAtOrBefore(now, zone);

    private IEnumerable<DateTimeOffset> DuesOn(DateOnly day, TimeZoneInfo zone) =>
        Days.Contains(day.DayOfWeek) ? Times.Select(at => DueOn(day, at, zone)) : [];

    /// <summary>The instant <paramref name="at"/> is reached on <paramref name="day"/> in <paramref name="zone"/>.</summary>
    public static DateTimeOffset DueOn(DateOnly day, TimeOnly at, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(at, DateTimeKind.Unspecified);
        // A time the clocks skip does not exist: the first one after it that does (the moment the clocks jump) is the due time.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        // A time the clocks repeat exists twice: the first of the two (the larger offset) is the due time.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>The day it is in <paramref name="zone"/> at <paramref name="now"/>: the schedule's day, and the failure notice's.</summary>
    internal static DateOnly LocalDay(DateTimeOffset now, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
}
