using Tankstat.Domain.Vehicles;
using Tankstat.Seeder;
using Tankstat.TestSupport;

namespace Tankstat.Seeder.UnitTests;

public class OdometerTrajectoryTests
{
    private static readonly DateOnly Day = new(2026, 6, 1);

    private static List<Refueling> Log(params (int Day, long Value)[] points)
    {
        var vehicleId = Guid.NewGuid();
        return [.. points.Select(p => TestData.Refueling(Guid.Empty, Guid.Empty, vehicleId, Day.AddDays(p.Day), odometer: p.Value))];
    }

    [Fact]
    public void WithoutALog_ThereIsNone() => Assert.Null(OdometerTrajectory.Of([], 40));

    [Fact]
    public void OnARefuellingsDay_ItsValue_AndBetweenTwo_AStraightLine()
    {
        var t = OdometerTrajectory.Of(Log((0, 10_000), (10, 10_500), (20, 10_600)), 40)!;

        Assert.Equal((10_000, 10_500, 10_600), (t.At(Day), t.At(Day.AddDays(10)), t.At(Day.AddDays(20))));
        Assert.Equal(10_250, t.At(Day.AddDays(5)));
        Assert.Equal(10_550, t.At(Day.AddDays(15)));
        Assert.Equal((Day, Day.AddDays(20), 10_600L), (t.First, t.Last, t.Latest));
    }

    [Fact]
    public void BeforeTheLog_ItGoesBackAtTheLogsPace_NeverBelowZero()
    {
        var t = OdometerTrajectory.Of(Log((0, 1_000), (10, 1_300)), 40)!; // 30 a day

        Assert.Equal(700, t.At(Day.AddDays(-10)));
        Assert.Equal(0, t.At(Day.AddDays(-100)));
        Assert.Equal(1_300 + 300, t.At(Day.AddDays(20))); // and on after it
    }

    [Fact]
    public void OneRefuelling_SaysNothingOfThePace_SoTheDefaultIsUsed() =>
        Assert.Equal(5_000 - 400, OdometerTrajectory.Of(Log((0, 5_000)), 40)!.At(Day.AddDays(-10)));

    [Fact]
    public void NeverGoesDown_AndDateAt_IsTheLastDayItHadNotPassedTheValue()
    {
        var t = OdometerTrajectory.Of(Log((0, 50_000), (4, 50_380), (17, 51_000), (21, 51_000), (30, 51_700)), 40)!;

        for (var d = -500; d < 40; d++) Assert.True(t.At(Day.AddDays(d)) <= t.At(Day.AddDays(d + 1)));
        foreach (var value in new long[] { 0, 1, 30_000, 49_999, 50_000, 50_001, 50_500, 51_000, 51_699, 51_700 })
        {
            var date = t.DateAt(value);
            Assert.True(t.At(date) <= value);
            Assert.True(date == t.Last || t.At(date.AddDays(1)) > value);
        }
        Assert.Equal(t.Last, t.DateAt(99_999)); // never past the log's last day
    }
}
