namespace Tankstat.Domain.Images;

/// <summary>The roots uploads are kept under (<c>Storage:Path</c> and <c>Storage:PhotosPath</c>); see <see cref="ImageFolders.RootsOf"/>.</summary>
[Flags]
public enum ImageRoots
{
    /// <summary>Avatars and vehicle pictures: few and small.</summary>
    Pictures = 1,

    /// <summary>The photos of refuellings and expenses and their drafts: they grow without limit.</summary>
    Photos = 2,
}
