using Tankstat.Application.Backups;

namespace Tankstat.Application.UnitTests;

/// <summary>A backup is compared with the database by the name of its last migration: each provider stamps the same migration with its own timestamp.</summary>
public class MigrationNamesTests
{
    [Theory]
    [InlineData("20261001054544_InitialCreate", "InitialCreate")]
    [InlineData("20261011134438_AddInstance", "AddInstance")]
    [InlineData("20261011134438_Add_Two_Words", "Add_Two_Words")] // only the first underscore ends the timestamp
    [InlineData("InitialCreate", "InitialCreate")] // an id without a timestamp is its own name
    [InlineData("", "")]
    public void TheNameIsWhatFollowsTheTimestamp(string id, string name) => Assert.Equal(name, MigrationNames.Of(id));

    [Fact]
    public void NoMigration_HasNoName()
    {
        Assert.Null(MigrationNames.Of(null));
        Assert.True(MigrationNames.Same(null, null)); // a database without migrations and a manifest without one are the same
        Assert.False(MigrationNames.Same(null, "20261011134438_AddInstance"));
        Assert.False(MigrationNames.Same("20261011134438_AddInstance", null));
    }

    [Fact]
    public void TheSameMigrationOfAnotherProvider_IsTheSame_AndAnotherMigrationIsNot()
    {
        Assert.True(MigrationNames.Same("20261011134435_AddInstance", "20261011134438_AddInstance")); // SQLite's and PostgreSQL's stamps
        Assert.True(MigrationNames.Same("20261011134435_AddInstance", "20261011134435_AddInstance"));
        Assert.False(MigrationNames.Same("20261011134435_AddInstance", "20261011134435_AddInstanceState"));
        Assert.False(MigrationNames.Same("20261009160039_AddPasswordResetIssuedAt", "20261011134438_AddInstance"));
        Assert.False(MigrationNames.Same("20261011134435_AddInstance", "20261011134435_addinstance")); // names are exact
    }
}
