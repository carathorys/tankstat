using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Stats;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class ChartServiceTests
{
    private static ChartConfig Config(ChartMetric metric = ChartMetric.TotalSpend) => new(metric, ChartGrouping.Month, ChartKind.Bar, ChartRange.Last6Months, false);

    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        return new Scene(w, alice, bob, await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, MeasurementUnits.Metric, default));
    }

    // ---- statistics ----------------------------------------------------------------------------------------

    [Fact]
    public async Task ChartAndSummary_AreCalculatedFromTheVehiclesLogs()
    {
        var s = await Setup();
        await s.W.RefuelingService.LogAsync(s.Car.Id, new RefuelingInput(new DateOnly(2026, 9, 5), 40, 100, "EUR", 1000, true, null), default);
        await s.W.RefuelingService.LogAsync(s.Car.Id, new RefuelingInput(new DateOnly(2026, 9, 25), 30, 80, "EUR", 1500, true, null), default);

        var chart = await s.W.Stats.ChartAsync(s.Car.Id, Config(ChartMetric.AverageConsumption), default);
        var summary = await s.W.Stats.SummaryAsync(s.Car.Id, default);

        Assert.Contains(chart.Series.Single().Points, p => p.Key == "2026-09" && p.Value == 6m);
        Assert.Equal((new DateOnly(2026, 9, 25), 1500L, 6m, "EUR"), (summary!.LastFillUpDate, summary.LatestOdometer, summary.AverageConsumption, summary.Currency));
    }

    [Fact]
    public async Task Statistics_AreOnlyForPeopleWhoCanSeeTheLogs()
    {
        var s = await Setup();
        s.W.Current.SignInAs(s.Bob);

        Assert.Null(await s.W.Stats.SummaryAsync(s.Car.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.Stats.ChartAsync(s.Car.Id, Config(), default));

        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        Assert.NotNull(await s.W.Stats.SummaryAsync(s.Car.Id, default));
    }

    [Fact]
    public async Task AnInvalidConfig_IsRejectedBeforeAnythingIsLoaded()
    {
        var s = await Setup();

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.Stats.ChartAsync(s.Car.Id, new ChartConfig(ChartMetric.FuelCost, ChartGrouping.Category, ChartKind.Bar, ChartRange.All, false), default));

        Assert.Equal("chart.categoryNeedsExpenses", error.Key);
    }

    // ---- saved charts --------------------------------------------------------------------------------------

    [Fact]
    public async Task ACreatorSeesOwnCharts_OthersOnlySeeSharedOnes()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("Mine", Config(), false), default);
        await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("For all", Config(ChartMetric.FuelVolume), true), default);

        Assert.Equal(["Mine", "For all"], (await s.W.ChartService.ListAsync(s.Car.Id, default)).Select(c => c.Title));
        s.W.Current.SignInAs(s.Bob);
        Assert.Equal(["For all"], (await s.W.ChartService.ListAsync(s.Car.Id, default)).Select(c => c.Title));
    }

    [Fact]
    public async Task ViewersMayMakeOwnCharts_ButSharingNeedsEditAccess()
    {
        var s = await Setup();
        s.W.Grants.Items.Add(AccessGrant.Create(s.Alice.Id, s.Bob.Id, AccessLevel.View));
        s.W.Current.SignInAs(s.Bob);

        var own = await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("Bob's", Config(), false), default);
        var denied = await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("Public", Config(), true), default));
        var denied2 = await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ChartService.UpdateAsync(own.Id, new ChartInput("Bob's", Config(), true), default));

        Assert.Equal(("chart.needEditToShare", "chart.needEditToShare"), (denied.Key, denied2.Key));
    }

    [Fact]
    public async Task StrangersCannotSeeOrMakeCharts_AndPrivateChartsAreInvisibleToOthers()
    {
        var s = await Setup();
        var mine = await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("Mine", Config(), false), default);
        s.W.Current.SignInAs(s.Bob);

        Assert.Empty(await s.W.ChartService.ListAsync(s.Car.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("x", Config(), false), default));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.ChartService.DeleteAsync(mine.Id, default));
    }

    [Fact]
    public async Task OnlyTheCreatorChangesAChart_ButDeleteAccessMayRemoveASharedOne()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        s.W.Current.SignInAs(s.Bob);
        var shared = await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("Bob's shared", Config(), true), default);

        s.W.Current.SignInAs(s.Alice); // the owner has Delete access
        Assert.True(await s.W.ChartService.CanEditAsync(shared, default));
        var notHers = await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ChartService.UpdateAsync(shared.Id, new ChartInput("Hijacked", Config(), true), default));
        await s.W.ChartService.DeleteAsync(shared.Id, default);

        Assert.Equal("chart.notYours", notHers.Key);
        Assert.Empty(s.W.Charts.Items);
    }

    [Fact]
    public async Task EditorsWithoutDeleteAccess_CannotRemoveSomeoneElsesSharedChart()
    {
        var s = await Setup();
        await s.W.Sharing.SetLogAccessAsync(s.Car.Id, s.Bob.Id, AccessLevel.Edit, default);
        var shared = await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("Alice's", Config(), true), default);
        s.W.Current.SignInAs(s.Bob);

        Assert.False(await s.W.ChartService.CanEditAsync(shared, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.W.ChartService.DeleteAsync(shared.Id, default));
    }

    [Fact]
    public async Task Update_ChangesTheRecipe()
    {
        var s = await Setup();
        var chart = await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("A", Config(), false), default);

        var updated = await s.W.ChartService.UpdateAsync(chart.Id, new ChartInput("B", Config(ChartMetric.FillUps), true), default);

        Assert.Equal(("B", ChartMetric.FillUps, true), (updated.Title, updated.Metric, updated.IsShared));
    }

    [Fact]
    public async Task InvalidCharts_AreRejected_AndThereIsALimitPerUser()
    {
        var s = await Setup();

        var invalid = await Assert.ThrowsAsync<DomainException>(() => s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("x", new ChartConfig(ChartMetric.FuelCost, ChartGrouping.Category, ChartKind.Bar, ChartRange.All, false), false), default));
        for (var i = 0; i < ChartService.MaxPerUserAndVehicle; i++) await s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput($"c{i}", Config(), false), default);
        var limit = await Assert.ThrowsAsync<DomainException>(() => s.W.ChartService.CreateAsync(s.Car.Id, new ChartInput("one too many", Config(), false), default));

        Assert.Equal(("chart.categoryNeedsExpenses", "chart.limit"), (invalid.Key, limit.Key));
    }
}
