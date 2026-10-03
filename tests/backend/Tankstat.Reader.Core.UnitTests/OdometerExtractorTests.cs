using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.UnitTests;

public class OdometerExtractorTests
{
    private static readonly OdometerExtractor Odometers = new(Lexicon.Default);

    private static string? Read(OcrPage text, OcrPage digits, long? last = null) =>
        Odometers.Extract([text], [digits], new ReadHints("hu", last, null, new DateOnly(2026, 10, 3)))?.Value;

    [Fact]
    public void TheBigNumberIsTheOdometer_NotTheTripMeterClockOrTemperature()
    {
        var text = OcrFixture.Page("TRIP A 456.7 km", "18:15 21°C", "!123456 km");
        var digits = OcrFixture.Page("4567", "1815 21", "!123456"); // the digit pass cannot see the dot, the colon or the degree sign

        Assert.Equal("123456", Read(text, digits));
    }

    [Fact]
    public void TheLatestKnownReading_DecidesBetweenCandidates()
    {
        var digits = OcrFixture.Page("!99999", "123456", "18 15", "21"); // with the clock and temperature digits as the usual size

        Assert.Equal("99999", Read(OcrPage.Empty, digits));             // the biggest digits win without a hint
        Assert.Equal("123456", Read(OcrPage.Empty, digits, last: 123000)); // an odometer never goes back
    }

    [Fact]
    public void DigitGroupsWithASmallGap_AreOneNumber_AndAUnitMayBeGlued()
    {
        Assert.Equal("123456", Read(OcrPage.Empty, OcrFixture.Page("!123 456")));
        Assert.Equal("98765", Read(OcrFixture.Page("98765km"), OcrPage.Empty));
    }

    [Fact]
    public void ShortNumbersAndNothing_GiveNoReading()
    {
        Assert.Null(Read(OcrPage.Empty, OcrFixture.Page("12")));
        Assert.Null(Read(OcrPage.Empty, OcrPage.Empty));
    }

    [Fact]
    public void ANumberSeenByMorePasses_IsMoreCertain()
    {
        var once = Odometers.Extract([OcrPage.Empty], [OcrFixture.Page("!123456")], new ReadHints("hu", null, null, new DateOnly(2026, 10, 3)))!;
        var twice = Odometers.Extract([OcrFixture.Page("!123456 km")], [OcrFixture.Page("!123456"), OcrFixture.Page("!123456")], new ReadHints("hu", null, null, new DateOnly(2026, 10, 3)))!;

        Assert.True(twice.Confidence > once.Confidence);
    }

    [Fact]
    public void BelowTheLatestReading_OrFarAboveIt_ThereIsNoReading_RatherThanAWrongOne()
    {
        Assert.Null(Read(OcrPage.Empty, OcrFixture.Page("!99999"), last: 123000));   // an odometer never goes back
        Assert.Null(Read(OcrPage.Empty, OcrFixture.Page("!999999"), last: 123000));  // nobody drives 877 000 km between two logs
        Assert.Equal("123456", Read(OcrPage.Empty, OcrFixture.Page("!123456"), last: 123000));
    }

    [Fact]
    public void AClockWhoseColonWasLost_DoesNotWinOverASmallerOdometer()
    {
        var digits = OcrFixture.Page("!0206", "54321", "12 3", "4"); // the clock ("02:06") drawn as large as the odometer

        Assert.Equal("54321", Read(OcrPage.Empty, digits));
    }
}
