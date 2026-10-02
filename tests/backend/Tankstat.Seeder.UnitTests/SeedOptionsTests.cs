using Tankstat.Seeder;

namespace Tankstat.Seeder.UnitTests;

public class SeedOptionsTests
{
    [Fact]
    public void Defaults()
    {
        var o = SeedOptionsParser.Parse([])!;

        Assert.Equal((25, 0, new IntRange(20, 20), 1234, false, null, null), (o.Vehicles, o.Trashed, o.RefuelingsPerVehicle, o.RandomSeed, o.AssumeYes, o.Provider, o.ConnectionString));
        Assert.Equal((new IntRange(0, 2), 15), (o.RecurringPerVehicle, o.Notifications));
    }

    [Fact]
    public void ParsesTheRecurringAndNotificationOptions()
    {
        var o = SeedOptionsParser.Parse(["--recurring", "1-3", "--notifications=40"])!;

        Assert.Equal((new IntRange(1, 3), 40), (o.RecurringPerVehicle, o.Notifications));
    }

    [Fact]
    public void ParsesEveryOption_InBothSyntaxes()
    {
        var spaced = SeedOptionsParser.Parse(["--vehicles", "200", "--trashed", "15", "--refuelings", "5-40", "--seed", "7", "--provider", "PostgreSql", "--connection", "Host=x;Password=y", "--yes"])!;
        var equals = SeedOptionsParser.Parse(["--vehicles=200", "--trashed=15", "--refuelings=5-40", "--seed=7", "--provider=PostgreSql", "--connection=Host=x;Password=y", "-y"])!;

        Assert.Equal(spaced, equals);
        Assert.Equal((200, 15, new IntRange(5, 40), 7, true, "PostgreSql", "Host=x;Password=y"), (spaced.Vehicles, spaced.Trashed, spaced.RefuelingsPerVehicle, spaced.RandomSeed, spaced.AssumeYes, spaced.Provider, spaced.ConnectionString));
    }

    [Fact]
    public void LaterOptionsOverrideEarlierOnes() =>
        Assert.Equal("b", SeedOptionsParser.Parse(["--connection", "a", "--connection", "b"])!.ConnectionString);

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpReturnsNull(string flag) => Assert.Null(SeedOptionsParser.Parse(["--vehicles", "3", flag]));

    [Theory]
    [InlineData("--vehicles", "-1")]
    [InlineData("--vehicles", "many")]
    [InlineData("--vehicles", "1000001")]
    [InlineData("--trashed", "x")]
    [InlineData("--refuelings", "10-5")]
    [InlineData("--refuelings", "a-b")]
    [InlineData("--recurring", "0-6")]
    [InlineData("--notifications", "-1")]
    [InlineData("--notifications", "10001")]
    [InlineData("--refuelings", "10001")]
    [InlineData("--seed", "1.5")]
    [InlineData("--nope", "1")]
    public void RejectsInvalidInput(string option, string value) =>
        Assert.Throws<SeedOptionsException>(() => SeedOptionsParser.Parse([option, value]));

    [Fact]
    public void RejectsAMissingValue() => Assert.Throws<SeedOptionsException>(() => SeedOptionsParser.Parse(["--vehicles"]));

    [Fact]
    public void RejectsTooManyVehiclesInTotal() => Assert.Throws<SeedOptionsException>(() => SeedOptionsParser.Parse(["--vehicles", "600000", "--trashed", "600000"]));

    [Fact]
    public void ARangeOfOneNumberIsAnExactCount() => Assert.Equal(new IntRange(7, 7), SeedOptionsParser.Parse(["--refuelings", "7"])!.RefuelingsPerVehicle);
}
