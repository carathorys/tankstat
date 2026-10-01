using Tankstat.Domain;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Domain.UnitTests;

public class VehicleTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public void Create_NormalizesNameAndPlate()
    {
        var v = Vehicle.Create(Owner, "  Octavia ", " abc-123 ", FuelType.Diesel);

        Assert.NotEqual(Guid.Empty, v.Id);
        Assert.Equal(Owner, v.OwnerId);
        Assert.Equal("Octavia", v.Name);
        Assert.Equal("ABC-123", v.LicensePlate);
        Assert.Equal(FuelType.Diesel, v.FuelType);
    }

    [Fact]
    public void Create_BlankPlate_BecomesNull() =>
        Assert.Null(Vehicle.Create(Owner, "Car", "  ", FuelType.Petrol).LicensePlate);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RequiresName(string name) =>
        Assert.Throws<DomainException>(() => Vehicle.Create(Owner, name, null, FuelType.Petrol));

    [Fact]
    public void Create_RejectsUnknownFuelType() =>
        Assert.Throws<DomainException>(() => Vehicle.Create(Owner, "Car", null, (FuelType)99));

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Update_ChangesAndNormalizesFields_KeepingIdentityAndOwner()
    {
        var v = Vehicle.Create(Owner, "Old", null, FuelType.Petrol);
        var id = v.Id;

        v.Update(" New ", " xy-1 ", FuelType.Lpg);

        Assert.Equal(("New", "XY-1", FuelType.Lpg), (v.Name, v.LicensePlate, v.FuelType));
        Assert.Equal((id, Owner), (v.Id, v.OwnerId));
    }

    [Fact]
    public void Update_AppliesTheSameRulesAsCreate()
    {
        var v = Vehicle.Create(Owner, "Car", null, FuelType.Petrol);

        Assert.Throws<DomainException>(() => v.Update(" ", null, FuelType.Petrol));
        Assert.Throws<DomainException>(() => v.Update("Car", null, (FuelType)99));
        Assert.Equal("Car", v.Name);
    }

    [Fact]
    public void Delete_SetsTimestamp_AndRestoreClearsIt()
    {
        var v = Vehicle.Create(Owner, "Car", null, FuelType.Petrol);
        Assert.False(v.IsDeleted);

        v.MarkDeleted(Now);
        Assert.True(v.IsDeleted);
        Assert.Equal(Now, v.DeletedAt);

        v.Restore();
        Assert.False(v.IsDeleted);
        Assert.Null(v.DeletedAt);
    }

    [Fact]
    public void Trash_RejectsDoubleDeleteRestoringLiveVehiclesAndEditing()
    {
        var v = Vehicle.Create(Owner, "Car", null, FuelType.Petrol);
        Assert.Throws<DomainException>(() => v.Restore());

        v.MarkDeleted(Now);
        Assert.Throws<DomainException>(() => v.MarkDeleted(Now));
        Assert.Throws<DomainException>(() => v.Update("Other", null, FuelType.Diesel));
    }
}
