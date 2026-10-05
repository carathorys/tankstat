using Tankstat.Domain.Recognition;

namespace Tankstat.Domain.UnitTests;

public class PhotoReadingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static PhotoReading Queued(ReadingPurpose purpose = ReadingPurpose.Refueling) =>
        PhotoReading.Queue(Guid.NewGuid(), purpose, "hu", 123_456, "HUF", Today, Now);

    private static PhotoReading Claimed(ReadingPurpose purpose = ReadingPurpose.Refueling)
    {
        var reading = Queued(purpose);
        reading.Claim(Now);
        return reading;
    }

    private static ReadingValue Value(ReadingFieldName name, string value, double confidence = 0.9) => new(name, value, confidence, ValueSource.Read);

    [Fact]
    public void Queue_KeepsTheHints_AndIsDueAtOnce()
    {
        var id = Guid.NewGuid();

        var reading = PhotoReading.Queue(id, ReadingPurpose.Expense, "de", null, "EUR", Today, Now);

        Assert.Equal((id, ReadingPurpose.Expense, ReadingStatus.Queued, "de", (long?)null, "EUR", Today, 0),
            (reading.Id, reading.Purpose, reading.Status, reading.Locale, reading.LastOdometer, reading.Currency, reading.Today, reading.Attempts));
        Assert.True(reading.IsDue(Now));
        Assert.Null(reading.Kind);
        Assert.Empty(reading.Values);
    }

    [Fact]
    public void Queue_RejectsAnUnknownPurpose()
    {
        var error = Assert.Throws<DomainException>(() => PhotoReading.Queue(Guid.NewGuid(), (ReadingPurpose)9, "en", null, null, Today, Now));

        Assert.Equal("reading.unknownPurpose", error.Key);
    }

    [Fact]
    public void ThePurpose_DecidesWhatThePhotoMayShow()
    {
        Assert.Equal([DocumentKind.Odometer, DocumentKind.FuelReceipt], Queued(ReadingPurpose.Refueling).AllowedKinds.Order());
        Assert.Equal([DocumentKind.Odometer, DocumentKind.ExpenseReceipt], Queued(ReadingPurpose.Expense).AllowedKinds.Order());
    }

    [Fact]
    public void Completing_KeepsTheResult_AndTheMostCertainValueOfEachName()
    {
        var reading = Claimed();

        reading.Complete("reader", "rules-1", DocumentKind.FuelReceipt,
            [Value(ReadingFieldName.Total, "24669", 0.7), Value(ReadingFieldName.Total, "24699", 0.95), Value(ReadingFieldName.Volume, "38.52")], Now.AddSeconds(3));

        Assert.Equal((ReadingStatus.Read, DocumentKind.FuelReceipt, "reader", "rules-1", (DateTimeOffset?)Now.AddSeconds(3), 1),
            (reading.Status, reading.Kind, reading.Provider, reading.ModelVersion, reading.ReadAt, reading.Attempts));
        Assert.Equal(["24699", "38.52"], reading.Values.Select(v => v.Value));
    }

    [Fact]
    public void AKindThePhotoMayNotShow_CountsAsUnknown_WithoutValues()
    {
        var reading = Claimed(ReadingPurpose.Expense);

        reading.Complete("reader", "rules-1", DocumentKind.FuelReceipt, [Value(ReadingFieldName.Total, "100")], Now);

        Assert.Equal((ReadingStatus.Read, DocumentKind.Unknown), (reading.Status, reading.Kind));
        Assert.Empty(reading.Values);
    }

    [Fact]
    public void AModelNamedByAWholePath_IsKeptAsFarAsTheColumnHolds()
    {
        var reading = Claimed();

        reading.Complete("openai-compatible", new string('m', 80), DocumentKind.Unknown, [], Now);

        Assert.Equal(new string('m', PhotoReading.MaxModelVersionLength), reading.ModelVersion);
    }

    [Fact]
    public void ConfidenceIsKeptBetweenZeroAndOne()
    {
        Assert.Equal((1.0, 0.0), (new ReadingValue(ReadingFieldName.Date, "2026-09-17", 1.7, ValueSource.Read).Confidence,
            new ReadingValue(ReadingFieldName.Date, "2026-09-17", -0.2, ValueSource.Read).Confidence));
    }

    [Fact]
    public void Retrying_WaitsLongerEachTime_AndGivesUpAfterTheLastAttempt()
    {
        var reading = Queued();
        var waits = new List<TimeSpan>();
        var clock = Now;

        for (var attempt = 1; attempt < PhotoReading.MaxAttempts; attempt++)
        {
            reading.Claim(clock);
            reading.Retry(clock);
            Assert.Equal(ReadingStatus.Queued, reading.Status);
            Assert.False(reading.IsDue(clock));
            waits.Add(reading.DueAt - clock);
            clock = reading.DueAt;
            Assert.True(reading.IsDue(clock));
        }
        reading.Claim(clock);
        reading.Retry(clock);

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(270)], waits);
        Assert.Equal((ReadingStatus.Failed, PhotoReading.MaxAttempts), (reading.Status, reading.Attempts));
    }

    [Fact]
    public void ARefusedPhoto_FailsAtOnce()
    {
        var reading = Claimed();

        reading.Fail();

        Assert.Equal((ReadingStatus.Failed, 1), (reading.Status, reading.Attempts));
    }

    [Fact]
    public void AnAbandonedAttempt_IsQueuedAgainAtOnce_UnlessItWasTheLast()
    {
        var reading = Claimed();
        reading.Abandon(Now.AddMinutes(5));
        Assert.True(reading.IsDue(Now.AddMinutes(5)));

        var last = Queued();
        for (var i = 1; i < PhotoReading.MaxAttempts; i++)
        {
            last.Claim(Now);
            last.Abandon(Now);
        }
        last.Claim(Now);
        last.Abandon(Now);

        Assert.Equal(ReadingStatus.Failed, last.Status);
    }

    [Fact]
    public void OnlyAQueuedReadingCanBeClaimed_AndOnlyAClaimedOneFinished()
    {
        var queued = Queued();
        var read = Claimed();
        read.Complete("reader", "rules-1", DocumentKind.Unknown, [], Now);

        Assert.Throws<InvalidOperationException>(() => queued.Complete("reader", "rules-1", DocumentKind.Unknown, [], Now));
        Assert.Throws<InvalidOperationException>(() => queued.Retry(Now));
        Assert.Throws<InvalidOperationException>(() => read.Claim(Now));
        Assert.Throws<InvalidOperationException>(read.Fail);
    }
}
