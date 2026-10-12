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

    /// <summary>
    /// The files of a root that belong in a backup, as (path in the archive below the part's folder, path on disk); none when the root does
    /// not exist. A folder the app cannot list (the lost+found of a disk mounted as the root, which is root's) holds nothing the app wrote: it
    /// is left out and <paramref name="unreadable"/> is told, rather than the whole backup failing on it. A folder gone meanwhile (a purged
    /// vehicle) is left out quietly. Hidden files are taken like any other.
    /// </summary>
    public static IEnumerable<(string Relative, string Full)> Walk(string root, Action<UnauthorizedAccessException> unreadable)
    {
        if (!Directory.Exists(root)) yield break;
        var folders = new Stack<string>();
        folders.Push(root);
        while (folders.Count > 0)
        {
            var folder = folders.Pop();
            string[] files, subfolders;
            try
            {
                files = Directory.GetFiles(folder);
                subfolders = Directory.GetDirectories(folder);
            }
            catch (UnauthorizedAccessException e)
            {
                unreadable(e);
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (Included(relative)) yield return (relative, file);
            }
            foreach (var subfolder in subfolders) folders.Push(subfolder);
        }
    }
}
