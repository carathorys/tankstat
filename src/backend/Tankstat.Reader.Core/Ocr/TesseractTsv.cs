using System.Globalization;

namespace Tankstat.Reader.Core.Ocr;

/// <summary>
/// Reads Tesseract's TSV output (<c>tesseract … tsv</c>): the page size from the level-1 row and every word (level 5) that has text.
/// Columns: level, page_num, block_num, par_num, line_num, word_num, left, top, width, height, conf, text.
/// </summary>
public static class TesseractTsv
{
    public static OcrPage Parse(string tsv)
    {
        int width = 0, height = 0;
        var words = new List<OcrWord>();
        foreach (var row in tsv.Split('\n'))
        {
            var cols = row.TrimEnd('\r').Split('\t');
            if (cols.Length < 12 || !int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)) continue;
            if (level == 1)
            {
                width = Int(cols[8]);
                height = Int(cols[9]);
                continue;
            }
            if (level != 5) continue;
            var text = cols[11].Trim();
            if (text.Length == 0) continue;
            if (!double.TryParse(cols[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || confidence < 0) continue;
            words.Add(new OcrWord(text, confidence, new Box(Int(cols[6]), Int(cols[7]), Int(cols[8]), Int(cols[9]))));
        }
        return new OcrPage(width, height, words);
    }

    private static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
