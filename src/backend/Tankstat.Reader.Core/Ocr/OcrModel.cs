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
public sealed record OcrWord(string Text, double Confidence, Box Box);

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
        Lines = GroupLines(words);
        MedianWordHeight = words.Count == 0 ? 0 : words.Select(w => w.Box.Height).Order().ElementAt(words.Count / 2);
    }

    public static OcrPage Empty { get; } = new(0, 0, []);

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<OcrWord> Words { get; }
    public IReadOnlyList<OcrLine> Lines { get; }
    public int MedianWordHeight { get; }

    /// <summary>
    /// Groups words into the lines a reader would see: a word joins the line it overlaps most (by at least half of the lower height).
    /// The OCR's own blocks are ignored on purpose: a receipt's label and its right-aligned amount often land in different blocks.
    /// </summary>
    private static IReadOnlyList<OcrLine> GroupLines(IReadOnlyList<OcrWord> words)
    {
        var lines = new List<(List<OcrWord> Words, Box Band)>();
        foreach (var word in words.OrderBy(w => w.Box.CenterY).ThenBy(w => w.Box.Left))
        {
            var best = -1;
            var bestOverlap = 0.5;
            for (var i = lines.Count - 1; i >= Math.Max(0, lines.Count - 4); i--)
            {
                var overlap = lines[i].Band.VerticalOverlap(word.Box);
                if (overlap >= bestOverlap)
                {
                    best = i;
                    bestOverlap = overlap;
                }
            }
            // The band stays the first word's: one tall word must not make its line swallow the next one.
            if (best < 0) lines.Add(([word], word.Box));
            else lines[best].Words.Add(word);
        }
        return lines.Select(l => new OcrLine(l.Words.OrderBy(w => w.Box.Left).ToList())).OrderBy(l => l.Box.Top).ToList();
    }
}
