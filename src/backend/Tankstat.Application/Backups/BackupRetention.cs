namespace Tankstat.Application.Backups;

/// <summary>
/// Which backups go after the app wrote one (<c>Backups:Keep</c>, <c>Backups:KeepDays</c>). Both are ceilings: a backup is removed when it
/// is beyond the newest <c>Keep</c> or older than <c>KeepDays</c>, whichever are set, and the newest one never goes. Only the app's own
/// files are candidates: named <c>tankstat-backup-...</c> by it and written by Tankstat, never an upload or a file copied in under another
/// name. The safety nets a restore writes are kept by a count of their own, <see cref="SafetyNetsKept"/>. With neither setting, nothing is
/// removed at all, safety nets included.
/// </summary>
public static class BackupRetention
{
    /// <summary>The prefix of the files the app names itself (<c>tankstat-backup-&lt;date&gt;T&lt;time&gt;Z.zip</c>).</summary>
    public const string OwnPrefix = "tankstat-backup-";

    /// <summary>How many safety nets (the backups written right before a restore) stay once retention is on.</summary>
    public const int SafetyNetsKept = 3;

    /// <summary>Whether a backup in the folder is one of the app's own, which retention and the missed-run check look at.</summary>
    public static bool IsOwn(RestorePoint point) =>
        point.File.StartsWith(OwnPrefix, StringComparison.Ordinal) && !point.Uploaded && point.Manifest.WrittenBy == BackupManifest.Tankstat;

    /// <summary>The newest backup the app wrote (a safety net does not count: it is not the backup a schedule asks for); null when there is none.</summary>
    public static DateTimeOffset? NewestOwn(IEnumerable<RestorePoint> points) =>
        points.Where(p => IsOwn(p) && !p.Manifest.SafetyNet).Select(p => (DateTimeOffset?)p.Manifest.CreatedAt).Max();

    public static IReadOnlyList<RestorePoint> ToRemove(IReadOnlyList<RestorePoint> points, int? keep, int? keepDays, DateTimeOffset now)
    {
        if (keep is null && keepDays is null) return [];
        var own = points.Where(IsOwn).OrderByDescending(p => p.Manifest.CreatedAt).ThenByDescending(p => p.File, StringComparer.Ordinal).ToList();
        var oldest = keepDays is { } days ? now.AddDays(-days) : (DateTimeOffset?)null;
        var backups = own.Where(p => !p.Manifest.SafetyNet).Skip(1) // the newest never goes
            .Select((p, i) => (Point: p, Rank: i + 1))
            .Where(x => x.Rank >= keep || x.Point.Manifest.CreatedAt < oldest)
            .Select(x => x.Point);
        var safetyNets = own.Where(p => p.Manifest.SafetyNet).Skip(SafetyNetsKept);
        return [.. backups, .. safetyNets];
    }
}
