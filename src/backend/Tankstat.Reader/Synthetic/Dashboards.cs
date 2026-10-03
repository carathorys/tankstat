using System.Globalization;
using SkiaSharp;

namespace Tankstat.Reader.Synthetic;

/// <summary>
/// Paints instrument-cluster displays: the odometer in large digits (seven-segment or a TFT-style font) with its unit, and around it what
/// dashboards also show and the reader must not take for the odometer: the trip meter (with a decimal), the clock, the temperature.
/// </summary>
internal static class Dashboards
{
    private static readonly SKColor[] DigitColors = [SKColors.White, new(255, 176, 0), new(120, 220, 255), new(140, 255, 140)];

    public static (SKBitmap Display, List<DrawnWord> Words, long Odometer) Paint(Random random)
    {
        const int width = 1600;
        const int height = 1000;
        var odometer = random.Next(0, 10) == 0 ? random.Next(1000, 9999) : random.Next(10_000, 250_000);
        var sevenSegment = random.Next(2) == 0;
        var color = DigitColors[random.Next(DigitColors.Length)];

        var display = new SKBitmap(width, height);
        using var canvas = new SKCanvas(display);
        var dark = (byte)random.Next(5, 30);
        using (var background = new SKPaint())
        {
            background.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height),
                [new SKColor(dark, dark, (byte)(dark + 6)), new SKColor((byte)(dark / 2), (byte)(dark / 2), (byte)(dark / 2))], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, background);
        }

        var writer = new Writer(canvas);
        using var digits = new SKFont(sevenSegment ? Fonts.SevenSegment : Fonts.SansBold, random.Next(150, 210));
        using var label = new SKFont(Fonts.SansBold, random.Next(46, 62));
        using var small = new SKFont(sevenSegment ? Fonts.SevenSegment : Fonts.SansBold, random.Next(56, 72));

        var value = odometer.ToString(CultureInfo.InvariantCulture);
        var x = random.Next(180, 420);
        var top = random.Next(260, 420);
        writer.Write(value, x, top, digits, color);
        writer.Write("km", x + Writer.Width(value, digits) + 30, top + Writer.Height(digits) - Writer.Height(label), label, color);

        var trip = (random.Next(0, 9999) / 10m).ToString("0.0", CultureInfo.InvariantCulture);
        var line = top + Writer.Height(digits) + random.Next(60, 110);
        writer.Write(random.Next(2) == 0 ? "TRIP A" : "A", x, line, label, color);
        writer.Write(trip, x + 260, line, small, color);
        writer.Write("km", x + 270 + Writer.Width(trip, small), line + 6, label, color);

        var clock = $"{random.Next(0, 24):00}:{random.Next(0, 60):00}";
        writer.Write(clock, random.Next(80, 200), random.Next(40, 120), small, color);
        var temperature = $"{random.Next(-9, 35).ToString(CultureInfo.InvariantCulture)}°C";
        writer.Write(temperature, width - random.Next(320, 420), random.Next(40, 120), label, color);
        return (display, writer.Words, odometer);
    }
}
