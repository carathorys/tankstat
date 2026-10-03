using System.Net.Http.Headers;
using SkiaSharp;

namespace Tankstat.Reader.IntegrationTests;

/// <summary>Small pictures made on the fly (no text: the OCR is faked where it matters).</summary>
internal static class TestImages
{
    public static byte[] Make(int width = 1600, int height = 1200, SKEncodedImageFormat format = SKEncodedImageFormat.Png, SKColor? background = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background ?? SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(width / 4f, height / 4f, width / 2f, height / 8f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static ByteArrayContent Content(byte[] bytes, string type = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }
}

/// <summary>Skips a test where the real Tesseract is not installed (it is in CI's reader job and in the image).</summary>
public sealed class TesseractFactAttribute : FactAttribute
{
    public TesseractFactAttribute()
    {
        if (!OnPath("tesseract")) Skip = "tesseract is not installed";
    }

    public static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(dir => File.Exists(Path.Combine(dir, program)));
}

/// <summary>Skips a test that runs shell scripts on Windows.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "uses a shell script";
    }
}
