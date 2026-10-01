using Tankstat.Domain;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Domain.UnitTests;

public class RefuelingTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void Create_SetsFields()
    {
        var vehicleId = Guid.NewGuid();
        var owner = Guid.NewGuid();

        var r = Refueling.Create(owner, vehicleId, Day, 42.5m, 80.75m, 12345, true);

        Assert.Equal(owner, r.OwnerId);
        Assert.Equal(vehicleId, r.VehicleId);
        Assert.Equal(Day, r.Date);
        Assert.Equal(42.5m, r.Liters);
        Assert.Equal(80.75m, r.TotalCost);
        Assert.Equal(12345, r.OdometerKm);
        Assert.True(r.IsFullTank);
    }

    [Theory]
    [InlineData(0, 10, 100)]
    [InlineData(-1, 10, 100)]
    [InlineData(10, -0.01, 100)]
    [InlineData(10, 10, -1)]
    public void Create_RejectsInvalidValues(double liters, double cost, int odometer) =>
        Assert.Throws<DomainException>(() => Refueling.Create(Guid.NewGuid(), Guid.NewGuid(), Day, (decimal)liters, (decimal)cost, odometer, false));
}
