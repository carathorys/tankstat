using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Domain.UnitTests;

public class ConsumptionCalculatorTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Car = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 1, 1);

    private static Refueling Log(int day, long odometer, decimal volume, bool full = true) =>
        TestData.Refueling(Owner, Owner, Car, Day.AddDays(day), volume, 10, odometer, full);

    private static decimal?[] Run(params Refueling[] logs)
    {
        ConsumptionCalculator.Apply(logs);
        return logs.Select(l => l.Consumption).ToArray();
    }

    [Fact]
    public void TheFirstFullFillUp_HasNoConsumption_TheNextOneDoes()
    {
        var values = Run(Log(0, 1000, 40), Log(10, 1500, 30));

        Assert.Equal([null, 6m], values); // 30 litres over 500 km = 6 per 100 km
    }

    [Fact]
    public void PartialFillUps_InBetween_AreCountedButHaveNoConsumptionThemselves()
    {
        var values = Run(Log(0, 1000, 40), Log(5, 1200, 10, full: false), Log(10, 1500, 20));

        Assert.Equal([null, null, 6m], values); // (10 + 20) litres over 500 km
    }

    [Fact]
    public void FuelAddedBeforeTheFirstFullFillUp_IsIgnored()
    {
        var values = Run(Log(0, 800, 15, full: false), Log(5, 1000, 40), Log(10, 1500, 30));

        Assert.Equal([null, null, 6m], values);
    }

    [Fact]
    public void LogsAreOrderedByDateThenOdometer_WhateverOrderTheyAreGiven()
    {
        var values = Run(Log(10, 1500, 30), Log(0, 1000, 40), Log(20, 2100, 36));

        Assert.Equal([6m, null, 6m], values);
    }

    [Fact]
    public void WithoutDistance_ThereIsNoConsumption()
    {
        var values = Run(Log(0, 1000, 40), Log(1, 1000, 30));

        Assert.Equal([null, null], values);
    }

    [Fact]
    public void ResultsAreRoundedToThreeDecimals_AndAnythingElseIsClearedAgain()
    {
        var logs = new[] { Log(0, 1000, 40), Log(10, 1300, 20), Log(11, 1400, 5, full: false) };
        ConsumptionCalculator.Apply(logs);
        Assert.Equal(6.667m, logs[1].Consumption);

        logs[1].SetConsumption(9); // a stale value on a partial one is wiped by the next run
        logs[2].SetConsumption(9);
        ConsumptionCalculator.Apply(logs);
        Assert.Null(logs[2].Consumption);
    }

    [Fact]
    public void OnlyTheLogsWhoseValueChanged_AreReturned()
    {
        var logs = new[] { Log(0, 1000, 40), Log(10, 1500, 30), Log(20, 2000, 30) };

        Assert.Equal(2, ConsumptionCalculator.Apply(logs).Count); // the second and third got values
        Assert.Empty(ConsumptionCalculator.Apply(logs)); // nothing new to save
    }

    [Fact]
    public void ThePartialFuelBetweenTwoFullOnesBelongsToTheLaterOne()
    {
        var values = Run(Log(0, 1000, 40), Log(10, 1500, 30), Log(12, 1600, 8, full: false), Log(20, 2000, 25));

        Assert.Equal([null, 6m, null, 6.6m], values); // second interval: (8 + 25) litres over 500 km
    }

    private static Refueling Waiting(int day, long? odometer, decimal? volume, bool full = true) =>
        Refueling.Create(Owner, Owner, Car, Day.AddDays(day), volume, null,
            odometer is { } o ? Odometers.OdometerReading.Create(Owner, Car, Day.AddDays(day), o) : null, full, readingPhotos: true);

    [Fact]
    public void AFillUpOfUnknownVolume_MakesItsIntervalUnknown_ButNotTheNextOne()
    {
        var values = Run(Log(0, 1000, 40), Waiting(5, 1200, null, full: false), Log(10, 1500, 20), Log(20, 2000, 30));

        Assert.Equal([null, null, null, 6m], values);
    }

    [Fact]
    public void AFullFillUpWithoutOdometer_CannotEndOrStartAnInterval()
    {
        var values = Run(Log(0, 1000, 40), Waiting(5, null, 30), Log(10, 1500, 30), Log(20, 2000, 30));

        Assert.Equal([null, null, null, 6m], values);
    }
}
