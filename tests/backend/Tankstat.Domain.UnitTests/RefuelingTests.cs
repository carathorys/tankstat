using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Domain.UnitTests;

public class RefuelingTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    [Fact]
    public void Create_SetsFields_AndLinksItsOwnReadingAndCost()
    {
        var vehicleId = Guid.NewGuid();

        var r = TestData.Refueling(Owner, Creator, vehicleId, Day, volume: 42.5m, totalCost: 80.75m, odometer: 12345, isFullTank: true, currency: "huf", note: "  road trip ");

        Assert.Equal((Owner, Creator, vehicleId, Day), (r.OwnerId, r.CreatedById, r.VehicleId, r.Date));
        Assert.Equal((42.5m, 80.75m, "HUF", 12345L, true, "road trip"), (r.Volume, r.TotalCost, r.Currency, r.Odometer, r.IsFullTank, r.Note));
        Assert.Equal(r.OdometerReading.Id, r.OdometerReadingId);
        Assert.Equal(r.Cost.Id, r.CostId);
        Assert.Equal((vehicleId, Day, 12345L), (r.OdometerReading.VehicleId, r.OdometerReading.Date, r.OdometerReading.Value));
        Assert.Equal((vehicleId, Day, 80.75m, "HUF"), (r.Cost.VehicleId, r.Cost.Date, r.Cost.Amount, r.Cost.Currency));
    }

    [Fact]
    public void PricePerUnit_IsDerivedFromCostAndVolume() =>
        Assert.Equal(1.9m, TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, volume: 40, totalCost: 76).PricePerUnit);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    public void Create_RejectsAVolumeThatIsNotPositive(double volume, double cost) =>
        Assert.Equal("refueling.volumePositive",
            Assert.Throws<DomainException>(() => TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, (decimal)volume, (decimal)cost)).Key);

    [Fact]
    public void Create_RejectsNegativeMoneyAndOdometer_AndBadCurrencies()
    {
        Assert.Equal("cost.negative", Assert.Throws<DomainException>(() => TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, totalCost: -0.01m)).Key);
        Assert.Equal("odometer.negative", Assert.Throws<DomainException>(() => TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, odometer: -1)).Key);
        Assert.Equal("money.currencyInvalid", Assert.Throws<DomainException>(() => TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, currency: "EURO")).Key);
        Assert.Equal("money.currencyInvalid", Assert.Throws<DomainException>(() => TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, currency: "")).Key);
    }

    [Fact]
    public void Create_RejectsALongNote() =>
        Assert.Equal("refueling.noteTooLong", Assert.Throws<DomainException>(() => TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day, note: new string('x', 501))).Key);

    [Fact]
    public void Create_RejectsReadingsAndCostsOfAnotherVehicle()
    {
        var vehicle = Guid.NewGuid();
        var other = Guid.NewGuid();

        var wrongReading = Assert.Throws<DomainException>(() => Refueling.Create(Owner, Creator, vehicle, Day, 10,
            Cost.Create(Owner, vehicle, Day, 1, "EUR"), OdometerReading.Create(Owner, other, Day, 1), true));
        var wrongCost = Assert.Throws<DomainException>(() => Refueling.Create(Owner, Creator, vehicle, Day, 10,
            Cost.Create(Owner, other, Day, 1, "EUR"), OdometerReading.Create(Owner, vehicle, Day, 1), true));

        Assert.Equal("refueling.wrongVehicle", wrongReading.Key);
        Assert.Equal("refueling.wrongVehicle", wrongCost.Key);
    }

    [Fact]
    public void Update_KeepsTheReadingAndTheCostInStep()
    {
        var r = TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day);
        var readingId = r.OdometerReadingId;
        var costId = r.CostId;

        r.Update(Day.AddDays(2), 30, 55, "USD", 2000, false, null);

        Assert.Equal((Day.AddDays(2), 30m, 55m, "USD", 2000L, false), (r.Date, r.Volume, r.TotalCost, r.Currency, r.Odometer, r.IsFullTank));
        Assert.Equal((readingId, Day.AddDays(2), 2000L), (r.OdometerReading.Id, r.OdometerReading.Date, r.OdometerReading.Value));
        Assert.Equal((costId, Day.AddDays(2), 55m, "USD"), (r.Cost.Id, r.Cost.Date, r.Cost.Amount, r.Cost.Currency));
    }

    [Fact]
    public void Trash_And_Restore_CarryTheReadingAndCostAlong()
    {
        var r = TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day);
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

        r.MarkDeleted(now);
        Assert.True(r.IsDeleted);
        Assert.Equal(now, r.DeletedAt);
        Assert.True(r.OdometerReading.IsDeleted);
        Assert.True(r.Cost.IsDeleted);

        r.Restore();
        Assert.False(r.IsDeleted);
        Assert.False(r.OdometerReading.IsDeleted);
        Assert.False(r.Cost.IsDeleted);
    }

    [Fact]
    public void Trash_Rules()
    {
        var r = TestData.Refueling(Owner, Creator, Guid.NewGuid(), Day);
        Assert.Equal("refueling.notTrashed", Assert.Throws<DomainException>(r.Restore).Key);

        r.MarkDeleted(DateTimeOffset.UtcNow);
        Assert.Equal("refueling.alreadyTrashed", Assert.Throws<DomainException>(() => r.MarkDeleted(DateTimeOffset.UtcNow)).Key);
        Assert.Equal("refueling.trashedCannotEdit", Assert.Throws<DomainException>(() => r.Update(Day, 1, 1, "EUR", 1, true, null)).Key);
    }
}

public class OdometerCostAndUnitsTests
{
    [Fact]
    public void Odometer_IsAnyNonNegativeNumber_WithNoUpperLimit()
    {
        Assert.Equal(0, OdometerValue.From(0).Value);
        Assert.Equal(long.MaxValue, OdometerValue.From(long.MaxValue).Value);
        Assert.Equal("odometer.negative", Assert.Throws<DomainException>(() => OdometerValue.From(-1)).Key);
    }

    [Fact]
    public void Reading_UpdatesAndFollowsItsOwnersLifecycle()
    {
        var reading = OdometerReading.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), 100);

        reading.Update(new DateOnly(2026, 2, 1), 250);
        reading.MarkDeleted(DateTimeOffset.UtcNow);
        Assert.True(reading.IsDeleted);
        reading.Restore();

        Assert.Equal((new DateOnly(2026, 2, 1), 250L, false), (reading.Date, reading.Value, reading.IsDeleted));
        Assert.Equal("odometer.negative", Assert.Throws<DomainException>(() => reading.Update(reading.Date, -5)).Key);
    }

    [Fact]
    public void Cost_StoresAmountAndCurrencyTogether()
    {
        var cost = Cost.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), 12.5m, " eur ");

        Assert.Equal((12.5m, "EUR"), (cost.Amount, cost.Currency));
        cost.Update(new DateOnly(2026, 1, 2), 0, "huf");
        Assert.Equal((0m, "HUF"), (cost.Amount, cost.Currency)); // free is fine
        Assert.Equal("cost.negative", Assert.Throws<DomainException>(() => cost.Update(cost.Date, -1, "EUR")).Key);
        Assert.Equal("money.currencyInvalid", Assert.Throws<DomainException>(() => cost.Update(cost.Date, 1, "E1R")).Key);
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData(" usd ", "USD")]
    [InlineData("huf", "HUF")]
    public void CurrencyCodes_AreNormalized(string input, string expected) => Assert.Equal(expected, CurrencyCode.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("12A")]
    public void CurrencyCodes_AreValidated(string? input) =>
        Assert.Equal("money.currencyInvalid", Assert.Throws<DomainException>(() => CurrencyCode.Normalize(input)).Key);

    [Fact]
    public void Units_CoverDistanceAndVolume_AndRejectUnknownValues()
    {
        var us = MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.UsGallons);

        Assert.Equal((DistanceUnit.Miles, VolumeUnit.UsGallons), (us.Distance, us.Volume));
        Assert.Equal((DistanceUnit.Kilometers, VolumeUnit.Liters), (MeasurementUnits.Metric.Distance, MeasurementUnits.Metric.Volume));
        Assert.Equal(MeasurementUnits.Metric, MeasurementUnits.Create(DistanceUnit.Kilometers, VolumeUnit.Liters)); // value equality
        Assert.Equal("vehicle.unknownUnit", Assert.Throws<DomainException>(() => MeasurementUnits.Create((DistanceUnit)9, VolumeUnit.Liters)).Key);
        Assert.Equal("vehicle.unknownUnit", Assert.Throws<DomainException>(() => MeasurementUnits.Create(DistanceUnit.Miles, (VolumeUnit)9)).Key);
    }

    [Fact]
    public void AVehicle_KeepsItsUnits()
    {
        var v = TestData.Vehicle(Guid.NewGuid(), units: MeasurementUnits.Create(DistanceUnit.Miles, VolumeUnit.ImperialGallons));

        Assert.Equal(VolumeUnit.ImperialGallons, v.Units.Volume);
        v.Update("Renamed", null, FuelType.Diesel, MeasurementUnits.Metric);
        Assert.Equal(DistanceUnit.Kilometers, v.Units.Distance);
    }
}
