using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.UnitTests;

public class ReceiptExtractorTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);
    private static readonly ReceiptExtractor Receipts = new(Lexicon.Default, new RuleScorer());

    private static Dictionary<string, string> Read(string kind, string locale, string? currencyHint, params string[] lines) =>
        Receipts.Extract(OcrFixture.Page(lines), kind, new ReadHints(locale, null, currencyHint, Today)).ToDictionary(f => f.Name, f => f.Value);

    [Fact]
    public void AHungarianFuelReceipt_GivesTotalLitresPricePerLitreCurrencyAndDate()
    {
        var read = Read(DocumentKinds.FuelReceipt, "hu", null,
            "Benzinkút Kft.",
            "1111 Budapest, Fő utca 1.",
            "Adószám: 12345678-2-42",
            "NYUGTA",
            "Gázolaj\t24 687 Ft",
            "38,52 l x 640,9 Ft/l",
            "!ÖSSZESEN:\t24 687 Ft",
            "Bankkártya\t24 687 Ft",
            "ÁFA 27%\t5 249 Ft",
            "2026.09.17 18:15",
            "Köszönjük a vásárlást!");

        Assert.Equal("24687", read[FieldNames.Total]);
        Assert.Equal("38.52", read[FieldNames.Volume]);
        Assert.Equal("640.9", read[FieldNames.UnitPrice]);
        Assert.Equal("HUF", read[FieldNames.Currency]);
        Assert.Equal("2026-09-17", read[FieldNames.Date]);
    }

    [Fact]
    public void AnEnglishFuelReceipt_ReadsPoundsAndADayFirstDate()
    {
        var read = Read(DocumentKinds.FuelReceipt, "en", null,
            "FUEL STATION LTD",
            "12 High Street",
            "Unleaded",
            "38.52 L @ 1.459 GBP/L\t£56.20",
            "!TOTAL\t£56.20",
            "VAT 20%\t£9.37",
            "17/09/2026 18:15",
            "Thank you");

        Assert.Equal(("56.2", "38.52", "1.459", "GBP", "2026-09-17"),
            (read[FieldNames.Total], read[FieldNames.Volume], read[FieldNames.UnitPrice], read[FieldNames.Currency], read[FieldNames.Date]));
    }

    [Fact]
    public void AGermanFuelReceipt_ReadsEurosWithDecimalCommas()
    {
        var read = Read(DocumentKinds.FuelReceipt, "de", null,
            "Tankstelle Süd GmbH",
            "Super E10",
            "38,52 l x 1,799 EUR/l\t69,30 EUR",
            "!SUMME EUR\t69,30",
            "MwSt 19%\t11,06",
            "17.09.2026 18:15",
            "Vielen Dank");

        Assert.Equal(("69.3", "38.52", "1.799", "EUR", "2026-09-17"),
            (read[FieldNames.Total], read[FieldNames.Volume], read[FieldNames.UnitPrice], read[FieldNames.Currency], read[FieldNames.Date]));
    }

    [Fact]
    public void AnExpenseReceipt_GivesTotalCurrencyDateAndTheMerchantAsTitle()
    {
        var read = Read(DocumentKinds.ExpenseReceipt, "de", null,
            "!Waschanlage Nord GmbH",
            "Hauptstraße 5",
            "Programm Premium\t89,90 €",
            "!SUMME\t89,90 €",
            "MwSt 19%\t14,35 €",
            "30.09.2026 12:01");

        Assert.Equal(("89.9", "EUR", "2026-09-30", "Waschanlage Nord GmbH"),
            (read[FieldNames.Total], read[FieldNames.Currency], read[FieldNames.Date], read[FieldNames.Title]));
        Assert.False(read.ContainsKey(FieldNames.Volume)); // litres are a fuel receipt's business
    }

    [Fact]
    public void ASubtotalOrVat_IsNotTheTotal_EvenWhenLarger()
    {
        var read = Read(DocumentKinds.ExpenseReceipt, "hu", null,
            "Autószerviz Kft.",
            "Részösszeg\t10 000 Ft",
            "Kedvezmény\t-500 Ft",
            "ÖSSZESEN\t9 500 Ft",
            "ÁFA 27%\t2 020 Ft");

        Assert.Equal("9500", read[FieldNames.Total]);
    }

    [Fact]
    public void TheMissingOneOfTotalLitresAndPrice_IsWorkedOutFromTheOtherTwo()
    {
        var fields = Receipts.Extract(OcrFixture.Page("Gázolaj 38,52 l", "ÖSSZESEN\t24 687 Ft"), DocumentKinds.FuelReceipt, new ReadHints("hu", null, null, Today));

        var price = Assert.Single(fields, f => f.Name == FieldNames.UnitPrice);
        Assert.Equal(("640.888", FieldSources.Derived), (price.Value, price.Source));
        Assert.True(price.Confidence < fields.Single(f => f.Name == FieldNames.Total).Confidence);
    }

    [Fact]
    public void LitresWithThreeDecimals_AreNotTakenForThousands()
    {
        var read = Read(DocumentKinds.FuelReceipt, "hu", null, "Benzin 95", "38,520 l x 640,9 Ft/l", "ÖSSZESEN\t24 687 Ft");

        Assert.Equal(("38.52", "24687"), (read[FieldNames.Volume], read[FieldNames.Total]));
    }

    [Fact]
    public void WithoutACurrencyOnTheReceipt_TheHintIsUsed_AsSuch()
    {
        var fields = Receipts.Extract(OcrFixture.Page("TOTAL\t12.50"), DocumentKinds.ExpenseReceipt, new ReadHints("en", null, "chf", Today));

        var currency = Assert.Single(fields, f => f.Name == FieldNames.Currency);
        Assert.Equal(("CHF", FieldSources.Hint), (currency.Value, currency.Source));
        Assert.Equal("12.5", fields.Single(f => f.Name == FieldNames.Total).Value);
    }

    [Fact]
    public void TotalsWithAKeywordAndAFittingSum_AreMoreCertainThanGuesses()
    {
        var sure = Receipts.Extract(OcrFixture.Page("38,52 l x 640,9 Ft/l", "ÖSSZESEN\t24 687 Ft"), DocumentKinds.FuelReceipt, new ReadHints("hu", null, null, Today));
        var guess = Receipts.Extract(OcrFixture.Page("Valami", "Más\t1 234 Ft"), DocumentKinds.ExpenseReceipt, new ReadHints("hu", null, null, Today));

        Assert.True(sure.Single(f => f.Name == FieldNames.Total).Confidence >= 0.85);
        Assert.True(guess.Single(f => f.Name == FieldNames.Total).Confidence < 0.8);
    }

    [Fact]
    public void AnEmptyPage_GivesNothingButTheCurrencyHint()
    {
        var fields = Receipts.Extract(OcrFixture.Page(), DocumentKinds.FuelReceipt, new ReadHints("hu", null, "HUF", Today));

        Assert.Equal([FieldNames.Currency], fields.Select(f => f.Name));
    }

    [Fact]
    public void TheSumCheckAllowsRounding_ButNotADifferentAmount()
    {
        Assert.True(ReceiptExtractor.Fits(24687, 38.52m * 640.9m, 0));
        Assert.True(ReceiptExtractor.Fits(56.20m, 38.52m * 1.459m, 2));
        Assert.False(ReceiptExtractor.Fits(5249, 38.52m * 640.9m, 0));
    }

    [Fact]
    public void OnALitresTimesPriceLine_ThePriceIsFound_EvenWithAGarbledUnit()
    {
        var read = Read(DocumentKinds.FuelReceipt, "hu", null, "Benzin 100\t29 650 Ft", "48.25 1 x 614.5 Fi/t", "!ÖSSZESEN:\t29 650 Ft");

        Assert.Equal(("48.25", "614.5", "29650"), (read[FieldNames.Volume], read[FieldNames.UnitPrice], read[FieldNames.Total]));
    }

    [Fact]
    public void LitresAndPriceThatDoNotFitTheTotal_AreBelowWhatTheAppFillsIn_TheTotalIsNot()
    {
        var fields = Receipts.Extract(OcrFixture.Page("9,12 l x 648,1 Ft/l", "!ÖSSZESEN:\t38 316 Ft", "Bankkártya\t38 316 Ft"),
            DocumentKinds.FuelReceipt, new ReadHints("hu", null, null, Today)).ToDictionary(f => f.Name);

        Assert.True(fields[FieldNames.Volume].Confidence < 0.6);
        Assert.True(fields[FieldNames.UnitPrice].Confidence < 0.6);
        Assert.True(fields[FieldNames.Total].Confidence >= 0.8);
    }

    [Fact]
    public void ANameHeadingTheReceipt_IsAFairSuggestion_AnAddressIsNoName()
    {
        var fields = Receipts.Extract(OcrFixture.Page("Fék Autószerviz Bt.", "7971 Budapest, Fő utca 44.", "Olajcsere\t25 000 Ft", "ÖSSZESEN\t25 000 Ft"),
            DocumentKinds.ExpenseReceipt, new ReadHints("hu", null, null, Today));

        var title = Assert.Single(fields, f => f.Name == FieldNames.Title);
        Assert.Equal("Fék Autószerviz Bt.", title.Value);
        Assert.True(title.Confidence >= 0.6);
        Assert.Null(Receipts.Extract(OcrFixture.Page("7971 Budapest, Fő utca 44.", "ÖSSZESEN\t25 000 Ft"), DocumentKinds.ExpenseReceipt,
            new ReadHints("hu", null, null, Today)).FirstOrDefault(f => f.Name == FieldNames.Title));
    }
}
