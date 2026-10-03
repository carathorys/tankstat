using Tankstat.Reader.Core.Ocr;

namespace Tankstat.Reader.Core.UnitTests;

public class TesseractTsvTests
{
    // What `tesseract … tsv` prints (trimmed): a page row, block/line rows with conf -1, and the words. Built from columns so no
    // editor can strip the tabs or the empty text cells at the line ends.
    private static readonly string Sample = string.Join('\n',
        Row("level", "page_num", "block_num", "par_num", "line_num", "word_num", "left", "top", "width", "height", "conf", "text"),
        Row("1", "1", "0", "0", "0", "0", "0", "0", "600", "800", "-1", ""),
        Row("2", "1", "1", "0", "0", "0", "10", "10", "300", "20", "-1", ""),
        Row("4", "1", "1", "1", "1", "0", "10", "10", "300", "20", "-1", ""),
        Row("5", "1", "1", "1", "1", "1", "10", "10", "120", "20", "95.5", "ÖSSZESEN:"),
        Row("5", "1", "2", "1", "1", "1", "480", "12", "30", "20", "91.25", "24"),
        Row("5", "1", "2", "1", "1", "2", "515", "12", "45", "20", "90", "687"),
        Row("5", "1", "2", "1", "1", "3", "565", "12", "25", "20", "89", "Ft"),
        Row("5", "1", "3", "1", "1", "1", "10", "60", "50", "20", "93", "2026.09.17"),
        Row("5", "1", "3", "1", "1", "2", "70", "60", "50", "20", "-1", ""),
        Row("5", "1", "3", "1", "1", "3", "130", "60", "50", "20", "88", "   "));

    private static string Row(params string[] columns) => string.Join('\t', columns);

    [Fact]
    public void ReadsThePageSizeAndTheWordsWithText()
    {
        var page = TesseractTsv.Parse(Sample);

        Assert.Equal((600, 800), (page.Width, page.Height));
        Assert.Equal(["ÖSSZESEN:", "24", "687", "Ft", "2026.09.17"], page.Words.Select(w => w.Text));
        Assert.Equal(new Box(480, 12, 30, 20), page.Words[1].Box);
        Assert.Equal(91.25, page.Words[1].Confidence);
    }

    [Fact]
    public void WordsOnTheSameVisualLine_FormOneLine_EvenAcrossTesseractBlocks()
    {
        var page = TesseractTsv.Parse(Sample);

        Assert.Equal(["ÖSSZESEN: 24 687 Ft", "2026.09.17"], page.Lines.Select(l => l.Text));
        Assert.Equal(new Box(10, 10, 580, 22), page.Lines[0].Box);
    }

    [Fact]
    public void WindowsLineEndingsAndEmptyOutput_AreFine()
    {
        Assert.Equal(5, TesseractTsv.Parse(Sample.Replace("\n", "\r\n")).Words.Count);
        Assert.Empty(TesseractTsv.Parse("").Words);
    }

    [Fact]
    public void OnATurnedPhoto_ALabelAndItsAmount_StayOneLine_AndTheNextLineStaysApart()
    {
        // The text falls 5 % to the right (about 3°): the amount at the right edge sits 25 px lower than its label at the left,
        // more than half a word height, so straight horizontal bands would split them. Tesseract's own line (key 1) shows the tilt.
        var words = new List<OcrWord>
        {
            new("Benzin", 90, new Box(10, 100, 80, 20), 1), new("95", 90, new Box(110, 104, 30, 20), 1), new("x", 90, new Box(160, 106, 10, 20), 1),
            new("24", 90, new Box(480, 124, 30, 20), 2), new("687", 90, new Box(520, 126, 40, 20), 2),
            new("ÖSSZESEN:", 90, new Box(10, 140, 110, 20), 3), new("24", 90, new Box(480, 164, 30, 20), 4), new("687", 90, new Box(520, 166, 40, 20), 4),
        };

        var page = new OcrPage(600, 800, words);

        Assert.InRange(page.Skew, 0.04, 0.06);
        Assert.Equal(["Benzin 95 x 24 687", "ÖSSZESEN: 24 687"], page.Lines.Select(l => l.Text));
    }

    [Fact]
    public void TesseractsLines_AreKeptOnTheWords() =>
        Assert.Equal(TesseractTsv.Parse(Sample).Words[1].LineKey, TesseractTsv.Parse(Sample).Words[2].LineKey);
}
