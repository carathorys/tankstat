using System.Runtime.InteropServices;
using SkiaSharp;
using Tankstat.Reader.Core.Ocr;

namespace Tankstat.Reader.Synthetic;

/// <summary>
/// Makes a drawn receipt or display look photographed: placed on a background, slightly turned, a little blurred, noisy and unevenly
/// lit, then encoded as WebP at the quality the browser uses. The words' rectangles follow every transform.
/// </summary>
internal static class Photos
{
    public static (byte[] Image, int Width, int Height, IReadOnlyList<OcrWord> Words) Receipt(SKBitmap paper, List<DrawnWord> words, Random random)
    {
        const int width = 1200;
        const int height = 1600;
        var scale = Math.Min(height * (0.80f + (float)random.NextDouble() * 0.15f) / paper.Height, width * 0.9f / paper.Width);
        var matrix = SKMatrix.Concat(
            SKMatrix.CreateTranslation(width / 2f + random.Next(-40, 40), height / 2f + random.Next(-40, 40)),
            SKMatrix.Concat(SKMatrix.CreateRotationDegrees((float)(random.NextDouble() * 5 - 2.5)),
                SKMatrix.Concat(SKMatrix.CreateScale(scale, scale), SKMatrix.CreateTranslation(-paper.Width / 2f, -paper.Height / 2f))));
        var tone = random.Next(70, 160);
        return Compose(width, height, new SKColor((byte)tone, (byte)(tone * 0.8), (byte)(tone * 0.6)), paper, matrix, words, random);
    }

    public static (byte[] Image, int Width, int Height, IReadOnlyList<OcrWord> Words) Dashboard(SKBitmap display, List<DrawnWord> words, Random random)
    {
        var matrix = SKMatrix.CreateRotationDegrees((float)(random.NextDouble() * 8 - 4), display.Width / 2f, display.Height / 2f);
        return Compose(display.Width, display.Height, SKColors.Black, display, matrix, words, random, glare: true);
    }

    private static (byte[], int, int, IReadOnlyList<OcrWord>) Compose(
        int width, int height, SKColor background, SKBitmap drawing, SKMatrix matrix, List<DrawnWord> words, Random random, bool glare = false)
    {
        using var photo = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(photo))
        {
            canvas.Clear(background);
            canvas.SetMatrix(matrix);
            using var image = SKImage.FromBitmap(drawing);
            var blur = 0.2f + (float)random.NextDouble() * 0.9f;
            using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(blur, blur) };
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            canvas.ResetMatrix();
            if (glare)
            {
                using var light = new SKPaint();
                var center = new SKPoint(random.Next(0, width), random.Next(0, height));
                light.Shader = SKShader.CreateRadialGradient(center, random.Next(200, 600), [new SKColor(255, 255, 255, (byte)random.Next(20, 70)), SKColors.Transparent], SKShaderTileMode.Clamp);
                canvas.DrawRect(0, 0, width, height, light);
            }
        }
        Grain(photo, random);

        using var encoded = SKImage.FromBitmap(photo);
        using var data = encoded.Encode(SKEncodedImageFormat.Webp, 85);
        var placed = words.Select(w =>
        {
            var r = matrix.MapRect(w.Rect);
            return new OcrWord(w.Text, 95, new Box((int)r.Left, (int)r.Top, (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)), w.Line);
        }).ToList();
        return (data.ToArray(), width, height, placed);
    }

    /// <summary>Uneven exposure and sensor noise, applied to the pixels.</summary>
    private static void Grain(SKBitmap photo, Random random)
    {
        var pixels = new byte[photo.RowBytes * photo.Height];
        Marshal.Copy(photo.GetPixels(), pixels, 0, pixels.Length);
        var brightness = random.Next(-20, 16);
        var contrast = 0.85 + random.NextDouble() * 0.25;
        var noise = random.Next(3, 11);
        var bytesPerPixel = photo.BytesPerPixel;
        for (var i = 0; i < pixels.Length; i += bytesPerPixel)
        {
            var jitter = random.Next(-noise, noise + 1);
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Clamp((int)((pixels[i + c] - 128) * contrast + 128 + brightness + jitter), 0, 255);
        }
        Marshal.Copy(pixels, 0, photo.GetPixels(), pixels.Length);
    }
}
