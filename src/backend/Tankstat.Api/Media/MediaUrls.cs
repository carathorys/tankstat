namespace Tankstat.Api.Media;

public static class MediaUrls
{
    /// <summary>
    /// The URL of an image. It contains the image's own id, which changes whenever the picture is replaced, so browsers may cache
    /// it forever and a new picture always gets a new address.
    /// </summary>
    public static string? Image(Guid? imageId) => imageId is { } id ? $"/media/{id:N}" : null;
}
