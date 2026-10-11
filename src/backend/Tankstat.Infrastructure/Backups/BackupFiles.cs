using Tankstat.Domain.Images;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Which files below a storage root go into a backup: everything, except the photo drafts (<c>vehicles/&lt;id&gt;/drafts</c>: photos of
/// entries not saved yet, which live a day). Paths inside the archive use <c>/</c>, whatever the system.
/// </summary>
internal static class BackupFiles
{
    private static readonly string Drafts = ImageFolders.PhotoDrafts(Guid.Empty).Split('/')[2]; // "drafts", as ImageFolders spells it

    /// <summary>Whether a file at this path (relative to its root, with <c>/</c>) belongs in a backup.</summary>
    public static bool Included(string relativePath)
    {
        var parts = relativePath.Split('/');
        return !(parts.Length > 3 && parts[0] == "vehicles" && parts[2] == Drafts);
    }

    /// <summary>The files of a root that belong in a backup, as (path in the archive below the part's folder, path on disk); none when the root does not exist.</summary>
    public static IEnumerable<(string Relative, string Full)> Walk(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (Included(relative)) yield return (relative, file);
        }
    }
}
