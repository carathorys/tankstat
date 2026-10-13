namespace Tankstat.Application;

/// <summary>How the folders of the settings (the pictures, the photos, the keys, the backups) are compared: in full, and whether one lies inside another.</summary>
public static class Folders
{
    public static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The full path, without a trailing separator (a relative one starts at the working directory).</summary>
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool Same(string a, string b) => string.Equals(a, b, PathComparison);

    /// <summary>Whether <paramref name="inner"/> (a full path) lies below <paramref name="outer"/> (a full path); the same folder is not inside itself.</summary>
    public static bool Inside(string inner, string outer) =>
        inner.StartsWith(Path.EndsInDirectorySeparator(outer) ? outer : outer + Path.DirectorySeparatorChar, PathComparison);

    /// <summary>The same folder, or one inside the other, either way: two folders that cannot be taken apart.</summary>
    public static bool Overlap(string a, string b) => Same(a, b) || Inside(a, b) || Inside(b, a);
}
