using System.Runtime.InteropServices;
using SkiaSharp;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Reading;

namespace Tankstat.Reader.Imaging;

/// <summary>
/// Turns an uploaded photo into what the OCR reads best: decoded (JPEG, PNG or WebP), turned upright by its EXIF orientation, enlarged
/// when small (Tesseract wants letters at least ~20 px high) and reduced to gray. The OCR only ever gets PNGs written here, never the
/// upload itself, so its image parser never sees an untrusted file.
/// </summary>
internal sealed class SkiaImagePreparer
{
    /// <summary>Larger pictures are refused before decoding (a small file can claim a huge size).</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>Pictures whose longer side is shorter than this are enlarged twice.</summary>
    public const int SmallEdge = 1200;

    public PreparedImage Prepare(ReadOnlyMemory<byte> data)
    {
        using var stream = new SKMemoryStream(data.ToArray());
        using var codec = SKCodec.Create(stream) ?? throw ReaderErrors.BadImage();
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels) throw ReaderErrors.BadImage();

        var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw ReaderErrors.BadImage();
            decoded = Replace(decoded, Orient(decoded, codec.EncodedOrigin));
            if (Math.Max(decoded.Width, decoded.Height) < SmallEdge)
                decoded = Replace(decoded, decoded.Resize(new SKImageInfo(decoded.Width * 2, decoded.Height * 2, SKColorType.Rgba8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKCubicResampler.Mitchell)));
            return new PreparedImage(decoded.Width, decoded.Height, Gray(decoded));
        }
        finally
        {
            decoded.Dispose();
        }
    }

    private static SKBitmap Replace(SKBitmap old, SKBitmap? replacement)
    {
        if (replacement is null || ReferenceEquals(replacement, old)) return old;
        old.Dispose();
        return replacement;
    }

    /// <summary>Upright by the EXIF orientation the browser normally applies already (other clients may not); mirrored ones are left as they are.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is not (SKEncodedOrigin.BottomRight or SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom)) return source;
        var turned = origin != SKEncodedOrigin.BottomRight;
        var target = new SKBitmap(new SKImageInfo(turned ? source.Height : source.Width, turned ? source.Width : source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        switch (origin)
        {
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, target.Width / 2f, target.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop: // shown turned a quarter clockwise
                canvas.Translate(target.Width, 0);
                canvas.RotateDegrees(90);
                break;
            default: // LeftBottom: a quarter counter-clockwise
                canvas.Translate(0, target.Height);
                canvas.RotateDegrees(-90);
                break;
        }
        canvas.DrawBitmap(source, 0, 0);
        return target;
    }

    /// <summary>Luminance, with transparent parts shown on white (the colours are premultiplied, so that is colour + 255 − alpha).</summary>
    private static byte[] Gray(SKBitmap rgba)
    {
        var (width, height, stride) = (rgba.Width, rgba.Height, rgba.RowBytes);
        var source = rgba.GetPixelSpan();
        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = source.Slice(y * stride, width * 4);
            for (var x = 0; x < width; x++)
            {
                var p = row.Slice(x * 4, 4);
                var luminance = (p[0] * 299 + p[1] * 587 + p[2] * 114) / 1000;
                gray[y * width + x] = (byte)Math.Min(255, luminance + 255 - p[3]);
            }
        }
        return gray;
    }
}

/// <summary>A prepared photo: gray pixels, written as PNG once per variant when the OCR asks for it.</summary>
internal sealed class PreparedImage(int width, int height, byte[] gray) : IPreparedImage
{
    private readonly Dictionary<ImageVariant, byte[]> _png = [];

    public int Width => width;
    public int Height => height;

    public ReadOnlyMemory<byte> Png(ImageVariant variant)
    {
        lock (_png)
        {
            if (!_png.TryGetValue(variant, out var png)) _png[variant] = png = Encode(variant == ImageVariant.Inverted ? Invert(gray) : gray);
            return png;
        }
    }

    /// <summary>The gray value at a point (for tests).</summary>
    public byte GrayAt(int x, int y) => gray[y * width + x];

    private static byte[] Invert(byte[] pixels) => pixels.Select(p => (byte)(255 - p)).ToArray();

    private byte[] Encode(byte[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        var target = bitmap.GetPixels();
        for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * width, target + y * bitmap.RowBytes, width);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
