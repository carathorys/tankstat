namespace Tankstat.Reader.Core.Ocr;

/// <summary>A rectangle on the page, in pixels of the image the OCR read.</summary>
public readonly record struct Box(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public double CenterY => Top + Height / 2.0;

    public static Box Union(IEnumerable<Box> boxes)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var b in boxes)
        {
            left = Math.Min(left, b.Left);
            top = Math.Min(top, b.Top);
            right = Math.Max(right, b.Right);
            bottom = Math.Max(bottom, b.Bottom);
        }
        return left == int.MaxValue ? default : new Box(left, top, right - left, bottom - top);
    }

    /// <summary>How much two boxes overlap vertically, relative to the lower of the two (0 to 1).</summary>
    public double VerticalOverlap(Box other)
    {
        var overlap = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return overlap <= 0 ? 0 : (double)overlap / Math.Max(1, Math.Min(Height, other.Height));
    }
}

/// <summary>One word as the OCR read it: its text, how sure it was (0 to 100) and where it is.</summary>
/// <param name="LineKey">The OCR's own line the word belongs to (0 when unknown); used to measure how much the photo is turned.</param>
public sealed record OcrWord(string Text, double Confidence, Box Box, int LineKey = 0);

/// <summary>Words that sit on the same visual line, left to right.</summary>
public sealed class OcrLine
{
    public OcrLine(IReadOnlyList<OcrWord> words)
    {
        Words = words;
        Text = string.Join(' ', words.Select(w => w.Text));
        Box = Box.Union(words.Select(w => w.Box));
        Confidence = words.Count == 0 ? 0 : words.Average(w => w.Confidence);
    }

    public IReadOnlyList<OcrWord> Words { get; }
    public string Text { get; }
    public Box Box { get; }
    public double Confidence { get; }
}

/// <summary>What one OCR pass found on a page, with the words grouped into visual lines from top to bottom.</summary>
public sealed class OcrPage
{
    public OcrPage(int width, int height, IReadOnlyList<OcrWord> words)
    {
        Width = width;
        Height = height;
        Words = words;
        Skew = MeasureSkew(words);
        Lines = GroupLines(words, Skew);
        MedianWordHeight = words.Count == 0 ? 0 : words.Select(w => w.Box.Height).Order().ElementAt(words.Count / 2);
    }

    public static OcrPage Empty { get; } = new(0, 0, []);

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<OcrWord> Words { get; }
    public IReadOnlyList<OcrLine> Lines { get; }
    public int MedianWordHeight { get; }

    /// <summary>How much the text rises (negative) or falls per pixel to the right: the tangent of the photo's tilt.</summary>
    public double Skew { get; }

    /// <summary>
    /// The tilt, from the OCR's own lines: the median slope of the lines that have words far enough apart. Clamped to about ±8°,
    /// beyond which the OCR would not have found the lines anyway.
    /// </summary>
    private static double MeasureSkew(IReadOnlyList<OcrWord> words)
    {
        var slopes = words.Where(w => w.LineKey != 0)
            .GroupBy(w => w.LineKey)
            .Select(g => g.OrderBy(w => w.Box.Left).ToList())
            .Where(l => l.Count >= 2 && l[^1].Box.Left - l[0].Box.Left >= 100)
            .Select(l => (l[^1].Box.CenterY - l[0].Box.CenterY) / (l[^1].Box.Left + l[^1].Box.Width / 2.0 - (l[0].Box.Left + l[0].Box.Width / 2.0)))
            .Order()
            .ToList();
        return slopes.Count == 0 ? 0 : Math.Clamp(slopes[slopes.Count / 2], -0.15, 0.15);
    }

    /// <summary>
    /// Groups words into the lines a reader would see, with the tilt taken out: a word joins the line whose (straightened) middle is
    /// within half a word height of its own. The OCR's own blocks are ignored on purpose: a receipt's label and its right-aligned
    /// amount often land in different blocks, and on a turned photo at different heights.
    /// </summary>
    private static IReadOnlyList<OcrLine> GroupLines(IReadOnlyList<OcrWord> words, double skew)
    {
        double Straight(OcrWord w) => w.Box.CenterY - skew * (w.Box.Left + w.Box.Width / 2.0);
        var lines = new List<(List<OcrWord> Words, double Middle, int Height)>();
        foreach (var word in words.OrderBy(Straight).ThenBy(w => w.Box.Left))
        {
            var best = -1;
            var bestDistance = double.MaxValue;
            for (var i = lines.Count - 1; i >= Math.Max(0, lines.Count - 4); i--)
            {
                var distance = Math.Abs(Straight(word) - lines[i].Middle);
                if (distance <= 0.5 * Math.Max(1, Math.Min(word.Box.Height, lines[i].Height)) && distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            // The line keeps its first word's middle and height: one tall word must not make it swallow the next line.
            if (best < 0) lines.Add(([word], Straight(word), word.Box.Height));
            else lines[best].Words.Add(word);
        }
        return lines.OrderBy(l => l.Middle).Select(l => new OcrLine(l.Words.OrderBy(w => w.Box.Left).ToList())).ToList();
    }
}
