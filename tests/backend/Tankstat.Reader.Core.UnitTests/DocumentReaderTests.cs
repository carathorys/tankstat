using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.UnitTests;

public class DocumentReaderTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static readonly string[] Receipt =
    [
        "Benzinkút Kft.", "NYUGTA", "Gázolaj\t24 687 Ft", "38,52 l x 640,9 Ft/l", "!ÖSSZESEN:\t24 687 Ft", "Bankkártya\t24 687 Ft", "2026.09.17 18:15",
    ];

    private static ReadRequest Request(params string[] kinds) => new(kinds.ToHashSet(), new ReadHints("hu", null, null, Today));

    [Fact]
    public void AReceipt_IsReadFromOneBlockOfText()
    {
        var ocr = new FakeOcr().On(ImageVariant.Normal, 6, OcrPurpose.Text, OcrFixture.Page(Receipt));

        var result = new DocumentReader(ocr).ReadAsync(new FakeImage(), Request(DocumentKinds.Odometer, DocumentKinds.FuelReceipt), default).Result;

        Assert.Equal((DocumentReader.ModelVersion, DocumentKinds.FuelReceipt), (result.ModelVersion, result.Kind));
        Assert.Equal(("24687", "38.52", "640.9"), (result.Value(FieldNames.Total), result.Value(FieldNames.Volume), result.Value(FieldNames.UnitPrice)));
        Assert.Equal([new OcrPass(ImageVariant.Normal, 6, OcrPurpose.Text)], ocr.Passes); // nothing more was needed
    }

    [Fact]
    public void WhenTheBlockHasNoTotal_SparseTextIsTried()
    {
        var ocr = new FakeOcr()
            .On(ImageVariant.Normal, 6, OcrPurpose.Text, OcrFixture.Page("Benzinkút Kft.", "NYUGTA", "2026.09.17 18:15"))
            .On(ImageVariant.Normal, 11, OcrPurpose.Text, OcrFixture.Page(Receipt));

        var result = new DocumentReader(ocr).ReadAsync(new FakeImage(), Request(DocumentKinds.FuelReceipt), default).Result;

        Assert.Equal("24687", result.Value(FieldNames.Total));
    }

    [Fact]
    public void ADashboard_IsReadWithDigitPasses_OnThePhotoItsInvertedAndItsThickenedCopies()
    {
        var ocr = new FakeOcr()
            .On(ImageVariant.Normal, 6, OcrPurpose.Text, OcrFixture.Page("12 3456"))
            .On(ImageVariant.Inverted, 11, OcrPurpose.Digits, OcrFixture.Page("!123456", "4567"));

        var result = new DocumentReader(ocr).ReadAsync(new FakeImage(), Request(DocumentKinds.Odometer, DocumentKinds.FuelReceipt), default).Result;

        Assert.Equal((DocumentKinds.Odometer, "123456"), (result.Kind, result.Value(FieldNames.Odometer)));
        Assert.Contains(new OcrPass(ImageVariant.Inverted, 11, OcrPurpose.Digits), ocr.Passes);
        Assert.Contains(new OcrPass(ImageVariant.Thickened, 11, OcrPurpose.Digits), ocr.Passes);     // seven-segment digits, gaps closed
        Assert.Contains(new OcrPass(ImageVariant.ThickenedMore, 11, OcrPurpose.Digits), ocr.Passes);
    }

    [Fact]
    public void WhenOnlyAnOdometerIsAccepted_NoReceiptPassIsRun()
    {
        var ocr = new FakeOcr().On(ImageVariant.Normal, 11, OcrPurpose.Digits, OcrFixture.Page("!54321"));

        var result = new DocumentReader(ocr).ReadAsync(new FakeImage(), Request(DocumentKinds.Odometer), default).Result;

        Assert.Equal("54321", result.Value(FieldNames.Odometer));
        Assert.DoesNotContain(ocr.Passes, p => p.PageSegmentation == 6);
    }

    [Fact]
    public void APhotoWithNothingReadable_IsUnknown_NotAnError()
    {
        var result = new DocumentReader(new FakeOcr()).ReadAsync(new FakeImage(), Request(DocumentKinds.Odometer, DocumentKinds.ExpenseReceipt), default).Result;

        Assert.Equal((DocumentKinds.Unknown, 0), (result.Kind, result.Fields.Count));
    }

    [Fact]
    public void ReceiptsAreToldApart_WhenBothKindsAreAccepted()
    {
        var both = new HashSet<string> { DocumentKinds.FuelReceipt, DocumentKinds.ExpenseReceipt };

        Assert.Equal(DocumentKinds.FuelReceipt, DocumentClassifier.ReceiptKind(OcrFixture.Page(Receipt), both, Lexicon.Default));
        Assert.Equal(DocumentKinds.ExpenseReceipt, DocumentClassifier.ReceiptKind(OcrFixture.Page("Parkoló Kft.", "Parkolás 2 óra\t800 Ft", "ÖSSZESEN\t800 Ft"), both, Lexicon.Default));
        Assert.Equal(DocumentKinds.FuelReceipt, DocumentClassifier.ReceiptKind(OcrFixture.Page("Parkoló Kft."), new HashSet<string> { DocumentKinds.FuelReceipt }, Lexicon.Default));
    }

    [Fact]
    public void ReceiptsScoreHigh_DashboardsLow()
    {
        Assert.True(DocumentClassifier.ReceiptScore(OcrFixture.Page(Receipt), Lexicon.Default, Today) >= 1.5);
        Assert.True(DocumentClassifier.ReceiptScore(OcrFixture.Page("!123456 km", "18:15"), Lexicon.Default, Today) < 1.5);
        Assert.Equal(0, DocumentClassifier.ReceiptScore(OcrPage.Empty, Lexicon.Default, Today));

        // What the OCR makes of a dashboard photo: lines of "text" with stray signs that happen to be currency markers, but no money.
        var noise = OcrFixture.Page("ERBEN On neo oO BoP", "saa ÄNNN IND Ale DA", "e £ ] i i A", "Kr", "e $", "Oil change and", "in 4800 km", "SENT ES SS Se s", "315193 164.6");
        Assert.True(DocumentClassifier.ReceiptScore(noise, Lexicon.Default, Today) < 1.5);

        // The same with the odd receipt word and an amount among the noise: lines the OCR itself doubts do not count.
        var doubted = OcrFixture.Page(30, "Sales 16 AB", "e 3 €", "ERBEN On neo oO BoP", "saa ÄNNN IND Ale DA", "Oil change and", "in 4800 km", "SENT ES SS Se s", "PET £");
        Assert.True(DocumentClassifier.ReceiptScore(doubted, Lexicon.Default, Today) < 1.5);
        Assert.True(DocumentClassifier.ReceiptScore(OcrFixture.Page(60, Receipt), Lexicon.Default, Today) >= 1.5);

        // A page the OCR doubts for the most part gets nothing for the few lines of "text" it is sure of (a weak total word, a stray "3 €").
        var mostlyNoise = OcrFixture.Page("= £ ir u SALE. a", "Sa u 3 €", "Oil change and", "ERBEN On neo oO BoP", "saa ÄNNN IND Ale DA", "SENT ES SS Se s",
            "?a EN er SER in 4800 2.2: 200", "?PET £", "?SSS $ A Mai ae in BEL", "?Mg [/ OLE", "?SRE We ESŐ Za ON", "?os = so essél", "?enema RETTET URN RIO");
        Assert.True(DocumentClassifier.ReceiptScore(mostlyNoise, Lexicon.Default, Today) < 1.5);
    }

    [Fact]
    public void ALikelyReceiptWithoutATotal_DoesNotStopTheOdometerBeingLookedFor()
    {
        // The block pass finds a total keyword, a date and lines of text, but no amount: it may still be a dashboard behind the noise.
        var block = OcrFixture.Page("Benzinkút Kft.", "Nyugta", "Köszönjük a vásárlást", "Viszontlátásra", "Adószám 12345678", "ÖSSZESEN: Ft", "2026.09.17 18:15");
        var ocr = new FakeOcr()
            .On(ImageVariant.Normal, 6, OcrPurpose.Text, block)
            .On(ImageVariant.Normal, 11, OcrPurpose.Digits, OcrFixture.Page("!315193"));

        var result = new DocumentReader(ocr).ReadAsync(new FakeImage(), Request(DocumentKinds.Odometer, DocumentKinds.FuelReceipt), default).Result;

        Assert.Equal((DocumentKinds.Odometer, "315193"), (result.Kind, result.Value(FieldNames.Odometer)));

        // With no odometer either, what was read of the receipt is still returned.
        var fallback = new DocumentReader(new FakeOcr().On(ImageVariant.Normal, 6, OcrPurpose.Text, block))
            .ReadAsync(new FakeImage(), Request(DocumentKinds.Odometer, DocumentKinds.FuelReceipt), default).Result;
        Assert.Equal((DocumentKinds.FuelReceipt, "2026-09-17"), (fallback.Kind, fallback.Value(FieldNames.Date)));
    }
}
