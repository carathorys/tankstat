using System.Globalization;

namespace Tankstat.Application.Backups;

/// <summary>
/// When a scheduled backup is due: every day at one time of the server's local time (<c>Backups:Every</c>). On the night the clocks skip
/// that time (02:00 on the spring night in much of Europe) it is due at the first time that exists after it; on the night they repeat it,
/// at the first of the two. A start that finds the last due time passed without a backup since (the app was down, or this is its first
/// start with a schedule, or the folder holds no backup of the app's own since) has missed one, and writes it at once.
/// </summary>
public static class BackupSchedule
{
    /// <summary>A time of day such as <c>02:00</c> or <c>2:30</c> (<c>H</c> takes one digit or two); null for nothing or anything else.</summary>
    public static TimeOnly? Parse(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    /// <summary>The first due time after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextAfter(DateTimeOffset now, TimeOnly at, TimeZoneInfo zone)
    {
        for (var day = LocalDay(now, zone); ; day = day.AddDays(1))
            if (DueOn(day, at, zone) is var due && due > now) return due;
    }

    /// <summary>The last due time at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset LastDueAtOrBefore(DateTimeOffset now, TimeOnly at, TimeZoneInfo zone)
    {
        for (var day = LocalDay(now, zone); ; day = day.AddDays(-1))
            if (DueOn(day, at, zone) is var due && due <= now) return due;
    }

    /// <summary>Whether the last due time passed without a backup since: <paramref name="lastBackup"/> is the newest the app wrote (null: none).</summary>
    public static bool Missed(DateTimeOffset? lastBackup, DateTimeOffset now, TimeOnly at, TimeZoneInfo zone) =>
        lastBackup is not { } last || last < LastDueAtOrBefore(now, at, zone);

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
