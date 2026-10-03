using Tankstat.Reader.Core.Extraction;
using Tankstat.Reader.Core.Ocr;
using Tankstat.Reader.Core.Text;

namespace Tankstat.Reader.Core.UnitTests;

public class OdometerExtractorTests
{
    private static readonly OdometerExtractor Odometers = new(Lexicon.Default);

    private static ReadHints Hints(long? last = null) => new("hu", last, null, new DateOnly(2026, 10, 3));

    private static string? Read(OcrPage text, OcrPage digits, long? last = null) => Read([text], [digits], last);

    private static string? Read(IReadOnlyList<OcrPage> text, IReadOnlyList<OcrPage> digits, long? last = null) =>
        Odometers.Extract(text, digits, Hints(last))?.Value;

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
        Assert.Null(Read([OcrPage.Empty], [OcrFixture.Page("7"), OcrFixture.Page("7"), OcrFixture.Page("7 7"), OcrFixture.Page("7")])); // a stray digit every pass sees
        Assert.Null(Read(OcrPage.Empty, OcrPage.Empty));
    }

    [Fact]
    public void GaugeLabelsAndStrayDigits_DoNotOutvoteTheOdometer()
    {
        // A cluster with a speedometer: every pass sees its labels (some run together) and odd digits, the odometer only some passes.
        var block = OcrFixture.Page("Oil change and", "in 4800 km", "315193 164.6", "80", "100", "120");
        var sparse = OcrFixture.Page("80", "100", "120", "60", "140", "in 4800 km\t200", "315193\t240");
        var labels = OcrFixture.Page("80", "100", "120", "!100120", "4800\t200", "7", "5", "0", "1", "240", "180");
        var seen = OcrFixture.Page("80", "100", "120", "!100120", "4800\t200", "7", "5", "2", "315193\t240");
        var misread = OcrFixture.Page("80", "100", "120", "!100120", "4800\t200", "7", "5", "2", "345193\t240");

        Assert.Equal("315193", Read([block, sparse], [labels, seen, labels, misread]));
    }

    [Theory]
    [InlineData("Oil change and inspection", "in 4800 km")]
    [InlineData("Olajcsere és szerviz", "4800 km múlva")]
    [InlineData("Inspektion", "in 4800 km")]
    public void AServiceCountdown_IsNotTheOdometer(string first, string second)
    {
        Assert.Null(Read(OcrFixture.Page(first, second), OcrPage.Empty));
        Assert.Equal("315193", Read(OcrFixture.Page(first, second, "315193"), OcrPage.Empty));
    }

    [Fact]
    public void TwoGaugeLabelsReadAsOneNumber_AreNotTheOdometer()
    {
        var digits = OcrFixture.Page("20", "40", "60", "80", "100", "120", "!100120", "54321");

        Assert.Equal("54321", Read(OcrPage.Empty, digits));
    }

    [Fact]
    public void DigitsOfDifferentSizes_AreNotOneNumber()
    {
        // The odometer with a big label of the speedometer right next to it.
        var page = new OcrPage(600, 300, [new OcrWord("315193", 90, new Box(10, 100, 72, 20)), new OcrWord("240", 90, new Box(90, 90, 60, 40))]);

        Assert.Equal("315193", Read(OcrPage.Empty, page));
    }

    [Fact]
    public void APassThatLostTheFirstDigit_ConfirmsThePassThatReadThemAll()
    {
        var text = OcrFixture.Page("315193 km");
        var partial = OcrFixture.Page(30, "!15193"); // the digit passes miss the first digit at the dark edge of the display, and doubt it

        var reading = Odometers.Extract([text], [partial, partial, partial], Hints())!;

        Assert.Equal("315193", reading.Value);
        Assert.True(reading.Confidence >= 0.6);
    }

    [Fact]
    public void WhatTheOcrItselfDoubts_CountsLess()
    {
        Assert.Equal("654321", Read([OcrFixture.Page(20, "!123456")], [OcrFixture.Page(90, "!654321")]));
    }

    [Fact]
    public void TwoReadingsNearlyAsLikely_LeaveTheReaderUnsure_UnlessTheLatestReadingDecides()
    {
        var sure = Odometers.Extract([OcrFixture.Page("315193 km")], [OcrFixture.Page("315193")], Hints())!;
        var torn = Odometers.Extract([OcrFixture.Page("315193 km")], [OcrFixture.Page("322313 km")], Hints())!; // one pass misread the digits
        var decided = Odometers.Extract([OcrFixture.Page("315193 km")], [OcrFixture.Page("322313 km")], Hints(last: 314500))!;

        Assert.True(sure.Confidence >= 0.6);
        Assert.True(torn.Confidence < 0.6 && torn.Confidence < sure.Confidence);
        Assert.Equal("315193", decided.Value);
        Assert.True(decided.Confidence >= 0.6);
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
