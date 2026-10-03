using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.UnitTests;

public class NumbersTests
{
    [Theory]
    [InlineData("24 669", "hu", "24669")]         // thousands grouped with a space
    [InlineData("24 669", "hu", "24669")]    // ... or a no-break space
    [InlineData("12.345,00", "hu", "12345")]      // both separators: the last one is the decimal one
    [InlineData("24,669.00", "en", "24669")]
    [InlineData("12 345,67", "hu", "12345.67")]
    [InlineData("38,52", "hu", "38.52")]
    [InlineData("640,9", "de", "640.9")]
    [InlineData("1.234.567", "de", "1234567")]    // the same separator twice groups thousands
    [InlineData("24.669", "hu", "24669")]         // ambiguous: Hungarian groups thousands with '.'
    [InlineData("38,520", "hu", "38.52")]         // ambiguous: ... and separates decimals with ','
    [InlineData("1.459", "en", "1.459")]          // ambiguous: English separates decimals with '.'
    [InlineData("24,669", "en", "24669")]         // ... and groups thousands with ','
    public void TheLocalesUsualReadingComesFirst(string raw, string locale, string expected) =>
        Assert.Equal(expected, Numbers.Format(Numbers.Interpret(raw, locale)[0].Value, 3));

    [Fact]
    public void AnAmbiguousNumber_HasBothReadings_WithTheirDecimals()
    {
        var readings = Numbers.Interpret("38,520", "hu");

        Assert.Equal([new NumberReading(38.520m, 3), new NumberReading(38520m, 0)], readings);
        Assert.Single(Numbers.Interpret("38,52", "hu"));
    }

    [Theory]
    [InlineData(24669.0, 2, "24669")]
    [InlineData(38.5249, 3, "38.525")]
    [InlineData(56.2, 2, "56.2")]
    [InlineData(640.888, 0, "641")]
    public void ValuesAreWrittenInvariant_WithoutTrailingZeros(double value, int decimals, string expected) =>
        Assert.Equal(expected, Numbers.Format((decimal)value, decimals));
}

public class DatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [Theory]
    [InlineData("2026.10.02.", "2026-10-02")]     // Hungarian
    [InlineData("2026. 10. 02.", "2026-10-02")]
    [InlineData("2026-09-17 18:15", "2026-09-17")]
    [InlineData("17.09.2026 18:15", "2026-09-17")] // German
    [InlineData("17.09.26", "2026-09-17")]
    [InlineData("17/09/2026", "2026-09-17")]       // day first; 17 cannot be a month
    [InlineData("Datum: 30.09.2026", "2026-09-30")]
    public void DatesInEveryFormat_AreFound(string text, string expected) =>
        Assert.Equal(expected, Assert.Single(Dates.Find(text, Today)).Date.ToString("yyyy-MM-dd"));

    [Fact]
    public void ADateThatCanBeReadBothWays_IsOfferedBothWays_DayFirstFirst()
    {
        var found = Dates.Find("02/03/2026", Today);

        Assert.Equal([new DateOnly(2026, 3, 2), new DateOnly(2026, 2, 3)], found.Select(f => f.Date));
    }

    [Theory]
    [InlineData("2026.12.24.")]        // in the future
    [InlineData("2022.01.01.")]        // more than three years ago
    [InlineData("2026.13.01.")]        // no such month
    [InlineData("Tel: 06 1 234 5678")] // no date at all
    public void ImpossibleOrImplausibleDates_AreIgnored(string text) => Assert.Empty(Dates.Find(text, Today));

    [Fact]
    public void TomorrowIsStillFine_ForReceiptsFromAnotherTimeZone() =>
        Assert.Single(Dates.Find("2026.10.04.", Today));

    [Fact]
    public void TimesOfDay_AreRecognised()
    {
        Assert.True(Dates.HasTime("2026.09.17 18:15"));
        Assert.True(Dates.HasTime("08:05:59"));
        Assert.False(Dates.HasTime("38,52 l"));
    }
}

public class CurrenciesTests
{
    [Theory]
    [InlineData("osszesen: 24 669 ft", "HUF")]
    [InlineData("24669ft", "HUF")]
    [InlineData("summe eur 69,30", "EUR")]
    [InlineData("1,799 €/l", "EUR")]
    [InlineData("total £56.20", "GBP")]
    [InlineData("640,9 ft/l", "HUF")]
    public void Markers_AreFoundAsIsoCodes(string folded, string code) => Assert.Equal(code, Currencies.Find(folded).First().Code);

    [Theory]
    [InlineData("benzinkut kft.")] // "ft" inside a word
    [InlineData("software")]
    [InlineData("38,52 l")]
    public void LettersInsideWords_AreNotCurrencies(string folded) => Assert.Empty(Currencies.Find(folded));

    [Fact]
    public void ForintsHaveNoDecimals_OthersHaveTwo() => Assert.Equal((0, 2, 2), (Currencies.Decimals("HUF"), Currencies.Decimals("EUR"), Currencies.Decimals("GBP")));
}

public class LexiconTests
{
    private static readonly Lexicon Words = Lexicon.Default;

    [Theory]
    [InlineData("ÖSSZESEN:", 1.0)]
    [InlineData("0SSZESEN", 1.0)]    // an OCR typo in a long word still counts
    [InlineData("Fizetendő összeg", 1.0)]
    [InlineData("SUMME EUR", 1.0)]
    [InlineData("Zu zahlen", 1.0)]
    [InlineData("AMOUNT DUE", 1.0)]
    [InlineData("Betrag", 0.6)]
    [InlineData("Részösszeg", 0.0)] // a subtotal is not the total
    [InlineData("SUBTOTAL", 0.0)]
    [InlineData("Benzin", 0.0)]
    public void TotalKeywords_HaveTheirWeight(string line, double weight) => Assert.Equal(weight, Words.TotalWeight(Folding.Fold(line)));

    [Theory]
    [InlineData("Részösszeg")]
    [InlineData("ÁFA 27%")]
    [InlineData("SUBTOTAL")]
    [InlineData("MwSt 19%")]
    [InlineData("Rückgeld")]
    public void NotTotalKeywords_AreRecognised(string line) => Assert.Equal(1.0, Words.NotTotalWeight(Folding.Fold(line)));

    [Fact]
    public void FuelUnitPriceDateOdometerAndTripWords_AreRecognised()
    {
        Assert.True(Words.HasFuelWord(Folding.Fold("Gázolaj")));
        Assert.True(Words.HasFuelWord(Folding.Fold("Super E10")));
        Assert.True(Words.HasUnitPriceWord(Folding.Fold("640,9 Ft/l")));
        Assert.True(Words.HasUnitPriceWord(Folding.Fold("1,799 EUR/l")));
        Assert.True(Words.HasDateWord(Folding.Fold("Dátum: 2026.09.17")));
        Assert.True(Words.HasOdometerWord(Folding.Fold("123456 km")));
        Assert.True(Words.HasTripWord(Folding.Fold("TRIP A")));
        Assert.False(Words.HasFuelWord(Folding.Fold("Parkolás")));
    }

    [Fact]
    public void ShortWordsMustMatchExactly() => Assert.Equal(0.0, Words.NotTotalWeight(Folding.Fold("ÁRA"))); // not "áfa"

    [Fact]
    public void FoldingDropsAccentsAndCase() => Assert.Equal("osszesen: fizetendo strasse", Folding.Fold("ÖSSZESEN: Fizetendő Straße"));
}
