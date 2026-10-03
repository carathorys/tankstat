using Tankstat.Reader.Core.Ocr;

namespace Tankstat.Reader.Core.UnitTests;

/// <summary>
/// Builds OCR pages from text laid out like a receipt: one string per line, words left to right; a tab sends the rest of the line to
/// the right edge (right-aligned amounts); a leading "!" makes the line twice as large (bold totals, big odometer digits).
/// </summary>
internal static class OcrFixture
{
    public const int Width = 600;
    private const int CharWidth = 12;
    private const int Height = 20;
    private const int Gap = 10;

    public static OcrPage Page(params string[] lines) => Page(90, lines);

    public static OcrPage Page(double confidence, params string[] lines)
    {
        var words = new List<OcrWord>();
        var y = 10;
        foreach (var raw in lines)
        {
            var big = raw.StartsWith('!');
            var scale = big ? 2 : 1;
            var parts = (big ? raw[1..] : raw).Split('\t');
            Place(words, parts[0], 10, y, scale, confidence);
            if (parts.Length > 1)
            {
                var right = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var width = (right.Sum(w => w.Length) + right.Length - 1) * CharWidth * scale;
                Place(words, parts[1], Width - 10 - width, y, scale, confidence);
            }
            y += Height * scale + Gap;
        }
        return new OcrPage(Width, y + 10, words);
    }

    private static void Place(List<OcrWord> words, string text, int x, int y, int scale, double confidence)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = word.Length * CharWidth * scale;
            words.Add(new OcrWord(word, confidence, new Box(x, y, width, Height * scale)));
            x += width + CharWidth * scale;
        }
    }
}

/// <summary>Answers each OCR pass with the page given for it (by variant, segmentation and purpose) and remembers the passes run.</summary>
internal sealed class FakeOcr : IOcrEngine
{
    private readonly Dictionary<OcrPass, OcrPage> _pages = [];

    public List<OcrPass> Passes { get; } = [];

    public FakeOcr On(ImageVariant variant, int psm, OcrPurpose purpose, OcrPage page)
    {
        _pages[new OcrPass(variant, psm, purpose)] = page;
        return this;
    }

    public Task<OcrPage> RecognizeAsync(ReadOnlyMemory<byte> png, OcrPass pass, CancellationToken ct)
    {
        Passes.Add(pass);
        return Task.FromResult(_pages.GetValueOrDefault(pass, OcrPage.Empty));
    }
}

internal sealed class FakeImage : IPreparedImage
{
    public int Width => 600;
    public int Height => 800;
    public ReadOnlyMemory<byte> Png(ImageVariant variant) => new byte[] { (byte)variant };
}
