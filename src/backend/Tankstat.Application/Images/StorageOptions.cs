using Microsoft.Extensions.Options;

namespace Tankstat.Application.Images;

/// <summary>Bound from the "Storage" section (e.g. <c>Storage__Path=/data/uploads</c>, <c>Storage__PhotosPath=/data/photos</c>).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploaded pictures (avatars, vehicle pictures); created when needed. Relative paths start at the working directory.</summary>
    public string Path { get; set; } = "uploads";

    /// <summary>
    /// Folder for the photos of logs (refuellings, expenses and their drafts), which grow without limit: kept apart, so a backup or a disk of
    /// their own can take them as a whole. Unset: <c>photos</c> next to <see cref="Path"/>. It may be the same folder as <see cref="Path"/>,
    /// never one inside the other.
    /// </summary>
    public string? PhotosPath { get; set; }

    /// <summary><see cref="Path"/> in full, without a trailing separator.</summary>
    public string PicturesRoot => Full(Path);

    /// <summary><see cref="PhotosPath"/> in full, without a trailing separator; unset, <c>photos</c> next to <see cref="PicturesRoot"/>.</summary>
    public string PhotosRoot => string.IsNullOrWhiteSpace(PhotosPath)
        ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PicturesRoot) ?? PicturesRoot, "photos")
        : Full(PhotosPath);

    /// <summary>Pictures and photos in one folder: nothing is ever moved between them.</summary>
    public bool OneRoot => string.Equals(PicturesRoot, PhotosRoot, PathComparison);

    /// <summary>One root inside the other: a backup or a disk could not take one without the other, so it is refused at start.</summary>
    public bool RootsNested => !OneRoot && (Inside(PhotosRoot, PicturesRoot) || Inside(PicturesRoot, PhotosRoot));

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Full(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

    private static bool Inside(string inner, string outer) =>
        inner.StartsWith(System.IO.Path.EndsInDirectorySeparator(outer) ? outer : outer + System.IO.Path.DirectorySeparatorChar, PathComparison);
}

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions o) => o.RootsNested
        ? ValidateOptionsResult.Fail("Storage:PhotosPath and Storage:Path must be the same folder or two separate ones, not one inside the other.")
        : ValidateOptionsResult.Success;
}
