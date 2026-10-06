using Tankstat.Domain.Settings;

namespace Tankstat.Domain.UnitTests;

/// <summary>Settings rows check shape and limits only; what an id means is the UI's business.</summary>
public class SettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static GridSettingsValues Values(IReadOnlyList<string>? order = null, IReadOnlyList<string>? hidden = null, int pageSize = 25, string sortColumn = "name", SortDirection direction = SortDirection.Asc) =>
        new(order ?? ["name", "licensePlate"], hidden ?? [], pageSize, sortColumn, direction);

    private static DomainException Catch(Action action) => Assert.Throws<DomainException>(action);

    [Theory]
    [InlineData("vehicles")]
    [InlineData("refueling-trash")]
    [InlineData("a")]
    [InlineData("grid2")]
    public void GridIds_AreLowerCaseWordsWithDigitsAndDashes_Trimmed(string id) => Assert.Equal(id, GridSettings.CheckGridId($" {id} "));

    [Theory]
    [InlineData("Vehicles")]
    [InlineData("1x")]
    [InlineData("a b")]
    [InlineData("-a")]
    [InlineData("")]
    [InlineData(null)]
    public void BadGridIds_AreRefused(string? id) => Assert.Equal("settings.gridIdInvalid", Catch(() => GridSettings.CheckGridId(id)).Key);

    [Fact]
    public void GridId_HasAMaximumLength()
    {
        Assert.Equal(new string('a', 40), GridSettings.CheckGridId(new string('a', 40)));
        Assert.Equal("settings.gridIdInvalid", Catch(() => GridSettings.CheckGridId(new string('a', 41))).Key);
    }

    [Fact]
    public void ColumnIds_AreCheckedForShape_AndDuplicatesAreDroppedKeepingTheFirst()
    {
        var grid = GridSettings.Create(Guid.NewGuid(), "vehicles", Values(order: ["name", "fuel_type", "a-1", "name"], hidden: ["licensePlate"]), Now);

        Assert.Equal(["name", "fuel_type", "a-1"], grid.Order);
        Assert.Equal(["licensePlate"], grid.Hidden);
        Assert.Equal("settings.columnIdInvalid", Catch(() => GridSettings.Create(Guid.NewGuid(), "vehicles", Values(order: ["a b"]), Now)).Key);
        Assert.Equal("settings.columnIdInvalid", Catch(() => GridSettings.Create(Guid.NewGuid(), "vehicles", Values(hidden: [""]), Now)).Key);
        Assert.Equal("settings.columnIdInvalid", Catch(() => GridSettings.Create(Guid.NewGuid(), "vehicles", Values(sortColumn: new string('x', 41)), Now)).Key);
    }

    [Fact]
    public void TooManyColumns_AreRefused_WithTheLimit()
    {
        var ids = Enumerable.Range(0, 51).Select(i => $"c{i}").ToList();

        var error = Catch(() => GridSettings.Create(Guid.NewGuid(), "vehicles", Values(order: ids), Now));

        Assert.Equal("settings.tooManyColumns", error.Key);
        Assert.Equal(GridSettings.MaxColumns, error.Args["max"]);
        Assert.Equal(50, GridSettings.Create(Guid.NewGuid(), "vehicles", Values(order: ids.Take(50).ToList()), Now).Order.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void PageSize_MustBeWithinTheLimits(int pageSize)
    {
        var error = Catch(() => GridSettings.Create(Guid.NewGuid(), "vehicles", Values(pageSize: pageSize), Now));

        Assert.Equal("settings.pageSizeInvalid", error.Key);
        Assert.Equal((GridSettings.MinPageSize, GridSettings.MaxPageSize), (error.Args["min"], error.Args["max"]));
    }

    [Fact]
    public void PageSize_LimitsAreInclusive()
    {
        Assert.Equal(1, GridSettings.Create(Guid.NewGuid(), "vehicles", Values(pageSize: 1), Now).PageSize);
        Assert.Equal(500, GridSettings.Create(Guid.NewGuid(), "vehicles", Values(pageSize: 500), Now).PageSize);
    }

    [Fact]
    public void UnknownSortDirection_IsRefused() =>
        Assert.Equal("settings.sortDirectionInvalid", Catch(() => GridSettings.Create(Guid.NewGuid(), "vehicles", Values(direction: (SortDirection)7), Now)).Key);

    [Fact]
    public void Update_ReplacesEverything_AndStampsTheTime()
    {
        var grid = GridSettings.Create(Guid.NewGuid(), "vehicles", Values(), Now);

        grid.Update(Values(order: ["b", "a"], hidden: ["a"], pageSize: 50, sortColumn: "b", direction: SortDirection.Desc), Now.AddMinutes(5));

        Assert.Equal(["b", "a"], grid.Order);
        Assert.Equal(["a"], grid.Hidden);
        Assert.Equal((50, "b", SortDirection.Desc, Now.AddMinutes(5)), (grid.PageSize, grid.SortColumn, grid.SortDirection, grid.UpdatedAt));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("en-GB")]
    [InlineData(" en ")]
    public void Languages_LookLikeLanguageCodes_Trimmed(string code)
    {
        var settings = UiSettings.Create(Guid.NewGuid(), Now);

        settings.SetLanguage(code, Now);

        Assert.Equal(code.Trim(), settings.Language);
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("english")]
    [InlineData("e")]
    [InlineData("en-gb")]
    [InlineData("en_GB")]
    public void BadLanguageCodes_AreRefused(string code) =>
        Assert.Equal("settings.languageInvalid", Catch(() => UiSettings.Create(Guid.NewGuid(), Now).SetLanguage(code, Now)).Key);

    [Fact]
    public void Settings_StartEmpty_AndForgetTheLanguageWithNull()
    {
        var settings = UiSettings.Create(Guid.NewGuid(), Now);
        Assert.Null(settings.NavOpen);
        Assert.Null(settings.Language);

        settings.SetNavOpen(false, Now.AddMinutes(1));
        settings.SetLanguage("hu", Now.AddMinutes(2));
        settings.SetLanguage(null, Now.AddMinutes(3));

        Assert.False(settings.NavOpen);
        Assert.Null(settings.Language);
        Assert.Equal(Now.AddMinutes(3), settings.UpdatedAt);
    }

    [Fact]
    public void VehicleOrder_PositionsStartAtZero_ANegativeOneIsAProgrammingError()
    {
        Assert.Equal(0, VehicleOrder.Create(Guid.NewGuid(), Guid.NewGuid(), 0).Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => VehicleOrder.Create(Guid.NewGuid(), Guid.NewGuid(), -1));
    }
}
