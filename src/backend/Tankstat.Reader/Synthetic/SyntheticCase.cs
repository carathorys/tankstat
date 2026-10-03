using SkiaSharp;
using Tankstat.Reader.Core;
using Tankstat.Reader.Core.Ocr;

namespace Tankstat.Reader.Synthetic;

/// <summary>
/// A generated photo and everything known about it: its kind, the hints a caller would send, the values a perfect reading returns,
/// and every word with its place, so a "perfect OCR" can test the rules without Tesseract. <see cref="Image"/> is WebP, like an upload
/// from the browser.
/// </summary>
public sealed record SyntheticCase(
    string Name, string Kind, ReadHints Hints, IReadOnlyDictionary<string, string> Expected, byte[] Image, int Width, int Height, IReadOnlyList<OcrWord> Words);

/// <summary>The embedded fonts (SIL OFL 1.1, see third_party/fonts).</summary>
internal static class Fonts
{
    public static SKTypeface Mono { get; } = Load("LiberationMono-Regular.ttf");
    public static SKTypeface SansBold { get; } = Load("LiberationSans-Bold.ttf");
    public static SKTypeface SevenSegment { get; } = Load("DSEG7Classic-Regular.ttf");

    private static SKTypeface Load(string file)
    {
        var stream = typeof(Fonts).Assembly.GetManifestResourceStream($"Tankstat.Reader.Fonts.{file}")
            ?? throw new InvalidOperationException($"The font {file} is not embedded.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"The font {file} cannot be read.");
    }
}

/// <summary>A drawn word: its text, its rectangle (in the canvas's own coordinates) and the line it was written on.</summary>
internal readonly record struct DrawnWord(string Text, SKRect Rect, int Line);

/// <summary>Text drawn on a canvas, remembered word by word.</summary>
internal sealed class Writer(SKCanvas canvas)
{
    private int _lines;

    public List<DrawnWord> Words { get; } = [];

    /// <summary>Draws <paramref name="text"/> with its left end at <paramref name="x"/> and its top at <paramref name="top"/>.</summary>
    public void Write(string text, float x, float top, SKFont font, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var metrics = font.Metrics;
        var baseline = top - metrics.Ascent;
        canvas.DrawText(text, x, baseline, font, paint);
        var space = font.MeasureText(" ");
        var line = ++_lines;
        foreach (var word in text.Split(' '))
        {
            var width = font.MeasureText(word);
            if (word.Length > 0) Words.Add(new DrawnWord(word, new SKRect(x, top, x + width, baseline + metrics.Descent), line));
            x += width + space;
        }
    }

    public static float Width(string text, SKFont font) => font.MeasureText(text);

    public static float Height(SKFont font) => font.Metrics.Descent - font.Metrics.Ascent;
}
