using Tankstat.Reader.Core.Ocr;

namespace Tankstat.Reader.Core.UnitTests;

public class OcrRepairTests
{
    private static string Repaired(params string[] words) =>
        OcrRepair.Repair(new OcrLine(words.Select((w, i) => new OcrWord(w, 90, new Box(i * 100, 0, 80, 20))).ToList())).Text;

    [Theory]
    [InlineData("11,~47", "11,47")]            // a space after the decimal comma
    [InlineData("TOTAL~£44~.53", "TOTAL £44.53")] // ... or before the decimal point
    [InlineData("38,52~1~x~640,9", "38,52 l x 640,9")]
    [InlineData("46,67~1L~x~657,4", "46,67 l x 657,4")]
    [InlineData("48.25~|1~x~614.5", "48.25 l x 614.5")]
    [InlineData("640,9~Ft/1", "640,9 Ft/l")]
    [InlineData("1,430~EUR/1l", "1,430 EUR/l")]
    [InlineData("20,35~1x~1,935~EUR/1", "20,35 l x 1,935 EUR/l")] // the litre sign glued to the multiplication sign
    [InlineData("57,54~Ux~691,0", "57,54 l x 691,0")]
    [InlineData("618,5~Ft/t", "618,5 Ft/l")]
    [InlineData("1,917~EUR/~1", "1,917 EUR/l")]
    public void TypicalOcrSlips_AreUndone(string words, string expected) => Assert.Equal(expected, Repaired(words.Split('~')));

    [Theory]
    [InlineData("Benzin~100")]
    [InlineData("Pump~1~Diesel")] // a one that is not between litres and the multiplication sign stays a one
    [InlineData("ÁFA~27%")]
    public void OtherLines_StayAsTheyAre(string words) => Assert.Equal(words.Replace('~', ' '), Repaired(words.Split('~')));

    [Fact]
    public void ALineWithNothingToRepair_IsReturnedItself()
    {
        var line = new OcrLine([new OcrWord("ÖSSZESEN", 90, new Box(0, 0, 80, 20))]);

        Assert.Same(line, OcrRepair.Repair(line));
    }

    [Fact]
    public void MergedWords_CoverBothBoxes()
    {
        var line = new OcrLine([new OcrWord("£44", 95, new Box(100, 10, 40, 20)), new OcrWord(".53", 80, new Box(150, 12, 30, 20))]);

        var word = Assert.Single(OcrRepair.Repair(line).Words);

        Assert.Equal(("£44.53", 80.0, new Box(100, 10, 80, 22)), (word.Text, word.Confidence, word.Box));
    }
}
