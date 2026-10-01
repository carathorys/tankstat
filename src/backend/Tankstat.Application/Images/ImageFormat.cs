using Tankstat.Domain;

namespace Tankstat.Application.Images;

/// <summary>
/// What an uploaded file really is, judged from its first bytes and never from the name or the declared type. Only JPEG, PNG and
/// WebP are accepted: no SVG (it can carry scripts) and nothing a browser might sniff into something else.
/// </summary>
public static class ImageFormat
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static string Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) throw new DomainException("image.empty", "No file was sent.");
        if (data.Length > MaxBytes) throw new DomainException("image.tooLarge", $"The picture can be at most {MaxBytes / 1024} KB.", new { MaxKb = MaxBytes / 1024 });

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";

        throw new DomainException("image.unsupportedType", "Only JPEG, PNG and WebP pictures are supported.");
    }
}
