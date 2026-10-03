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
    }
}
