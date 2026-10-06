using Tankstat.Application.Imports;
using Tankstat.Application.Refuelings;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

/// <summary>The stored consumption follows the logs: it is refreshed whenever one of the vehicle's logs changes.</summary>
public class ConsumptionTests
{
    private static readonly DateOnly Day = new(2026, 8, 1);

    private sealed record Scene(World W, Vehicle Car);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("alice@x.co"));
        return new Scene(w, await w.VehicleService.AddAsync("Car", null, FuelType.Petrol, MeasurementUnits.Metric, default));
    }

    private static Task<Refueling> Log(Scene s, int day, long odometer, decimal volume, bool full = true) =>
        s.W.RefuelingService.LogAsync(s.Car.Id, new RefuelingInput(Day.AddDays(day), volume, 50, "EUR", odometer, full, null), default);

    private static decimal?[] Stored(Scene s) => s.W.Refuelings.Items.Where(r => !r.IsDeleted).OrderBy(r => r.Date).ThenBy(r => r.Odometer).Select(r => r.Consumption).ToArray();

    [Fact]
    public async Task AddingLogs_StoresTheConsumptionOfTheNewFullFillUp()
    {
        var s = await Setup();

        await Log(s, 0, 1000, 40);
        var second = await Log(s, 10, 1500, 30);

        Assert.Equal(6m, second.Consumption); // the returned log already has it
        Assert.Equal([null, 6m], Stored(s));
    }

    [Fact]
    public async Task LoggingAFillUpInTheMiddle_ChangesTheLaterOnesToo()
    {
        var s = await Setup();
        await Log(s, 0, 1000, 40);
        await Log(s, 20, 2000, 60); // 60 l over 1000 km = 6

        await Log(s, 10, 1400, 20); // splits the interval: 20 l / 400 km = 5, then 60 l / 600 km = 10

        Assert.Equal([null, 5m, 10m], Stored(s));
    }

    [Fact]
    public async Task EditingALog_RefreshesItAndItsNeighbours()
    {
        var s = await Setup();
        await Log(s, 0, 1000, 40);
        var middle = await Log(s, 10, 1500, 30);
        await Log(s, 20, 2000, 25);

        var updated = await s.W.RefuelingService.UpdateAsync(middle.Id, new RefuelingInput(middle.Date, 50, 50, null, 1500, true, null), default);

        Assert.Equal(10m, updated.Consumption);
        Assert.Equal([null, 10m, 5m], Stored(s));
    }

    [Fact]
    public async Task MakingAFullFillUpPartial_MergesTheIntervals()
    {
        var s = await Setup();
        await Log(s, 0, 1000, 40);
        var middle = await Log(s, 10, 1500, 30);
        await Log(s, 20, 2000, 24);

        await s.W.RefuelingService.UpdateAsync(middle.Id, new RefuelingInput(middle.Date, 30, 50, null, 1500, false, null), default);

        Assert.Equal([null, null, 5.4m], Stored(s)); // (30 + 24) litres over 1000 km
    }

    [Fact]
    public async Task TrashingAndRestoring_MoveTheBoundariesBack_AndForth()
    {
        var s = await Setup();
        await Log(s, 0, 1000, 40);
        var middle = await Log(s, 10, 1500, 30);
        await Log(s, 20, 2000, 24);

        await s.W.RefuelingService.DeleteAsync(middle.Id, default);
        Assert.Equal([null, 2.4m], Stored(s)); // 24 l over 1000 km: the trashed log no longer splits the interval

        await s.W.RefuelingService.RestoreAsync(middle.Id, default);
        Assert.Equal([null, 6m, 4.8m], Stored(s));
    }

    [Fact]
    public async Task ARestoredLog_ComesBackWithItsConsumption()
    {
        var s = await Setup();
        await Log(s, 0, 1000, 40);
        var second = await Log(s, 10, 1500, 30);
        await s.W.RefuelingService.DeleteAsync(second.Id, default);

        var restored = await s.W.RefuelingService.RestoreAsync(second.Id, default);

        Assert.Equal(6m, restored.Consumption);
    }

    [Fact]
    public async Task ImportingManyRows_CalculatesOnce_AndTheValuesAreRight()
    {
        var s = await Setup();
        const string csv = """
            "## Log"
            "Data","Odo (km)","Fuel (litres)","Full","Price (optional)"
            "2026-08-01","1000","40","1","50"
            "2026-08-11","1500","30","1","50"
            "2026-08-21","2000","25","1","50"
            """;
        var upload = await s.W.Imports.UploadAsync("fuelio", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), default);

        await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), new ImportOptions("EUR", false), default);

        Assert.Equal([null, 6m, 5m], Stored(s));
    }

    [Fact]
    public async Task MarkingAMissedFillUp_LeavesItsIntervalOut_UntilTheMarkGoes_AndAnUpdateWithoutItKeepsIt()
    {
        var s = await Setup();
        await Log(s, 0, 1000, 40);
        var middle = await Log(s, 10, 1500, 30);
        await Log(s, 20, 2000, 25);

        await s.W.RefuelingService.UpdateAsync(middle.Id, new RefuelingInput(middle.Date, 30, 50, null, 1500, true, null, MissedPreviousFillUp: true), default);
        Assert.Equal([null, null, 5m], Stored(s)); // the fill-up before the middle one was never logged: its interval is unknown

        var kept = await s.W.RefuelingService.UpdateAsync(middle.Id, new RefuelingInput(middle.Date, 30, 50, null, 1500, true, "a note"), default);
        Assert.True(kept.MissedPreviousFillUp);
        Assert.Equal([null, null, 5m], Stored(s));

        await s.W.RefuelingService.UpdateAsync(middle.Id, new RefuelingInput(middle.Date, 30, 50, null, 1500, true, null, MissedPreviousFillUp: false), default);
        Assert.Equal([null, 6m, 5m], Stored(s));
    }

    [Fact]
    public async Task ImportingAFillUpThatFollowsAMissedOne_LeavesItsIntervalOut()
    {
        var s = await Setup();
        const string csv = """
            "## Log"
            "Data","Odo (km)","Fuel (litres)","Full","Price (optional)","Missed"
            "2026-08-01","1000","40","1","50","0"
            "2026-08-11","2000","30","1","50","1"
            "2026-08-21","2500","25","1","50","0"
            """;
        var upload = await s.W.Imports.UploadAsync("fuelio", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), default);

        await s.W.Imports.CommitAsync(upload.Token, new ImportTarget(s.Car.Id, null), new ImportOptions("EUR", false), default);

        Assert.Equal([null, null, 5m], Stored(s));
        Assert.Equal([false, true, false], s.W.Refuelings.Items.OrderBy(r => r.Date).Select(r => r.MissedPreviousFillUp));
    }
}
