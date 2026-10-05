using Tankstat.Application.Recognition;
using Tankstat.Domain.Recognition;

namespace Tankstat.Application.UnitTests;

/// <summary>What the app believes of a model's reading: the checks it can make decide, the model's own confidence only lowers the result.</summary>
public class ReadingChecksTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static RecognitionRequest Hints(long? lastOdometer = 200_000, string? currency = "HUF") =>
        new(new byte[] { 1 }, "image/jpeg", new HashSet<DocumentKind> { DocumentKind.Odometer, DocumentKind.FuelReceipt }, "hu", lastOdometer, currency, Today);

    private static RecognizedValue Read(ReadingFieldName name, string value, double confidence = 0.9) => new(name, value, confidence, ValueSource.Read);

    private static string Shown(IEnumerable<RecognizedValue> values) => string.Join(" ", values.Select(v => $"{v.Name}={v.Value}@{v.Confidence}"));

    [Fact]
    public void OnlyTheValuesThatKindOfPhotoShows_AreKept_Normalised()
    {
        var kept = ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, "212345"), Read(ReadingFieldName.Total, "24687")], Hints());

        Assert.Equal("Odometer=212345@0.9", Shown(kept));
        Assert.Empty(ReadingChecks.Apply(DocumentKind.Unknown, [Read(ReadingFieldName.Odometer, "212345")], Hints()));
        Assert.Empty(ReadingChecks.Apply(DocumentKind.FuelReceipt, [Read(ReadingFieldName.Volume, "38,52")], Hints())); // not a number the app keeps
        Assert.Equal("Total=24687", Shown(ReadingChecks.Apply(DocumentKind.ExpenseReceipt, [Read(ReadingFieldName.Total, "24687.00", 1)], Hints())).Split('@')[0]);
    }

    [Theory]
    [InlineData(199_999, null)] // below the latest reading: an odometer never goes back
    [InlineData(200_000, 0.9)]
    [InlineData(250_000, 0.9)]
    [InlineData(250_001, 0.5)] // possible between two logs, but not to be believed on its own
    [InlineData(300_000, 0.5)]
    [InlineData(300_001, null)] // more than a hundred thousand since the last log: misread
    public void AnOdometer_IsBelievedAboveTheLatestReading_AndDoubtedFarAboveIt(long value, double? confidence)
    {
        var kept = ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, value.ToString())], Hints());

        Assert.Equal(confidence, kept.SingleOrDefault()?.Confidence);
    }

    [Fact]
    public void AVehiclesFirstOdometer_HasNothingToBeCheckedAgainst_AndIsTakenAsRead()
    {
        var kept = ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, "999999", 0.8)], Hints(lastOdometer: null));

        Assert.Equal("Odometer=999999@0.8", Shown(kept));
    }

    [Theory]
    [InlineData("2026-10-02", true)] // tomorrow, in some time zone
    [InlineData("2026-10-03", false)]
    [InlineData("2023-10-01", true)]
    [InlineData("2023-09-30", false)]
    public void AReceiptsDate_MustBeRecent_AndNeverInTheFuture(string date, bool kept) =>
        Assert.Equal(kept, ReadingChecks.Apply(DocumentKind.FuelReceipt, [Read(ReadingFieldName.Date, date)], Hints()).Count == 1);

    [Theory]
    [InlineData(ReadingFieldName.Volume, "0.2")]
    [InlineData(ReadingFieldName.Volume, "401")]
    [InlineData(ReadingFieldName.Total, "10000000")]
    [InlineData(ReadingFieldName.UnitPrice, "100000")]
    public void AnAmountNoFillUpHas_IsDropped(ReadingFieldName name, string value) =>
        Assert.Empty(ReadingChecks.Apply(DocumentKind.FuelReceipt, [Read(name, value)], Hints()));

    [Fact]
    public void LitresTimesPrice_MustFitTheTotal_OrBothAreDoubted_WhileTheTotalIsKept()
    {
        RecognizedValue[] fitting = [Read(ReadingFieldName.Total, "24687"), Read(ReadingFieldName.Volume, "38.52"), Read(ReadingFieldName.UnitPrice, "640.9"), Read(ReadingFieldName.Currency, "HUF")];
        RecognizedValue[] misread = [Read(ReadingFieldName.Total, "30000"), Read(ReadingFieldName.Volume, "38.52"), Read(ReadingFieldName.UnitPrice, "640.9"), Read(ReadingFieldName.Currency, "HUF")];

        Assert.Equal("Total=24687@0.9 Volume=38.52@0.9 UnitPrice=640.9@0.9 Currency=HUF@0.9", Shown(ReadingChecks.Apply(DocumentKind.FuelReceipt, fitting, Hints())));
        Assert.Equal("Total=30000@0.9 Volume=38.52@0.5 UnitPrice=640.9@0.5 Currency=HUF@0.9", Shown(ReadingChecks.Apply(DocumentKind.FuelReceipt, misread, Hints())));
    }

    [Fact]
    public void HowCloseIsClose_DependsOnTheCurrency_TheReceiptsOrElseTheHint()
    {
        // 0.5 l at 2.04 makes 1.02: two cents off a total of 1.00 is a misread in euros, nothing in forints (whole units)
        RecognizedValue[] values = [Read(ReadingFieldName.Total, "1.00"), Read(ReadingFieldName.Volume, "0.5"), Read(ReadingFieldName.UnitPrice, "2.04")];

        Assert.Equal(0.5, ReadingChecks.Apply(DocumentKind.FuelReceipt, [.. values, Read(ReadingFieldName.Currency, "EUR")], Hints()).Single(v => v.Name == ReadingFieldName.Volume).Confidence);
        Assert.Equal(0.9, ReadingChecks.Apply(DocumentKind.FuelReceipt, values, Hints(currency: "HUF")).Single(v => v.Name == ReadingFieldName.Volume).Confidence);
        Assert.Equal(0.5, ReadingChecks.Apply(DocumentKind.FuelReceipt, values, Hints(currency: null)).Single(v => v.Name == ReadingFieldName.Volume).Confidence);
    }

    [Fact]
    public void TheModelsOwnConfidence_OnlyEverLowersTheResult()
    {
        Assert.Equal(0.3, ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, "200100", 0.3)], Hints()).Single().Confidence);
        Assert.Equal(0.3, ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, "260000", 0.3)], Hints()).Single().Confidence); // doubted, and the model's 0.3 is less still
        Assert.Equal(1.0, ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, "200100", 7)], Hints()).Single().Confidence);
        Assert.Equal(ReadingChecks.Doubtful, ReadingChecks.Apply(DocumentKind.Odometer, [Read(ReadingFieldName.Odometer, "200100", double.NaN)], Hints()).Single().Confidence);
    }
}
