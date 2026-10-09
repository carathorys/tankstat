using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Vehicles;
using Tankstat.TestSupport;

namespace Tankstat.Domain.UnitTests;

/// <summary>Errors carry stable keys and arguments so clients can translate them; the English message is only a fallback.</summary>
public class ErrorKeyTests
{
    private static DomainException Catch(Action action) => Assert.Throws<DomainException>(action);

    [Fact]
    public void Vehicle_Errors()
    {
        var owner = Guid.NewGuid();
        var vehicle = TestData.Vehicle(owner, "Car", null, FuelType.Petrol);

        Assert.Equal("vehicle.nameRequired", Catch(() => TestData.Vehicle(owner, " ", null, FuelType.Petrol)).Key);
        var fuel = Catch(() => TestData.Vehicle(owner, "Car", null, (FuelType)99));
        Assert.Equal("vehicle.unknownFuelType", fuel.Key);
        Assert.Equal("99", fuel.Args["value"]);
        Assert.Equal("vehicle.notTrashed", Catch(() => vehicle.Restore()).Key);
        vehicle.MarkDeleted(DateTimeOffset.UtcNow);
        Assert.Equal("vehicle.alreadyTrashed", Catch(() => vehicle.MarkDeleted(DateTimeOffset.UtcNow)).Key);
        Assert.Equal("vehicle.trashedCannotEdit", Catch(() => vehicle.Update("x", null, FuelType.Lpg, MeasurementUnits.Metric)).Key);
    }

    [Fact]
    public void Refueling_Errors()
    {
        var day = new DateOnly(2026, 10, 1);

        Assert.Equal("refueling.volumePositive", Catch(() => TestData.Refueling(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), day, volume: 0)).Key);
        Assert.Equal("cost.negative", Catch(() => TestData.Refueling(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), day, totalCost: -1)).Key);
        Assert.Equal("odometer.negative", Catch(() => TestData.Refueling(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), day, odometer: -1)).Key);
    }

    [Fact]
    public void Password_Errors_CarryTheLimits()
    {
        var tooShort = Catch(() => PasswordPolicy.Validate("short"));
        Assert.Equal("password.tooShort", tooShort.Key);
        Assert.Equal(PasswordPolicy.MinLength, tooShort.Args["min"]);

        var tooLong = Catch(() => PasswordPolicy.Validate(new string('x', PasswordPolicy.MaxLength + 1)));
        Assert.Equal("password.tooLong", tooLong.Key);
        Assert.Equal(PasswordPolicy.MaxLength, tooLong.Args["max"]);
    }

    [Fact]
    public void User_And_Access_Errors()
    {
        Assert.Equal("user.emailInvalid", Catch(() => User.CreateLocal("nope", null, false)).Key);
        Assert.Equal("user.subjectRequired", Catch(() => User.CreateExternal(UserProvider.Oidc, " ", null, null, false)).Key);
        Assert.Equal("user.passwordLocalOnly", Catch(() => User.CreateExternal(UserProvider.Oidc, "s", null, null, false).SetPasswordHash("x")).Key);
        var id = Guid.NewGuid();
        Assert.Equal("access.selfGrant", Catch(() => AccessGrant.Create(id, id, AccessLevel.View)).Key);
        Assert.Equal("access.levelRequired", Catch(() => AccessGrant.Create(Guid.NewGuid(), Guid.NewGuid(), AccessLevel.None)).Key);
        var unknown = Catch(() => AccessSettings.Default().SetDefaultLevelForOthers((AccessLevel)42));
        Assert.Equal("access.unknownLevel", unknown.Key);
        Assert.Equal("42", unknown.Args["level"]);
    }

    [Fact]
    public void Message_StaysAvailableAsEnglishFallback() =>
        Assert.Equal("Vehicle name is required.", Catch(() => TestData.Vehicle(Guid.NewGuid(), "", null, FuelType.Petrol)).Message);
}
