using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Application.Settings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Settings;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class SettingsRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static GridSettingsValues Values(int pageSize = 25, SortDirection direction = SortDirection.Asc) => new(["name", "licensePlate"], ["licensePlate"], pageSize, "name", direction);

    [Fact]
    public async Task UiSettings_UpsertKeepsOneRowPerUser_AndRoundTrips()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUiSettingsRepository>();
        var user = Guid.NewGuid();
        var settings = UiSettings.Create(user, Now);
        settings.SetNavOpen(false, Now);
        await repo.SaveAsync(settings, default);
        var again = (await repo.FindAsync(user, default))!;
        again.SetLanguage("hu", Now.AddMinutes(1));
        await repo.SaveAsync(again, default);

        var loaded = (await repo.FindAsync(user, default))!;

        Assert.Equal((false, "hu", Now.AddMinutes(1)), (loaded.NavOpen, loaded.Language, loaded.UpdatedAt));
        Assert.Null(await repo.FindAsync(Guid.NewGuid(), default));
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await ctx.UiSettings.CountAsync());
    }

    [Fact]
    public async Task Grids_RoundTripTheirLists_ListById_AndReportRemoval()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUiSettingsRepository>();
        var user = Guid.NewGuid();
        await repo.SaveGridAsync(GridSettings.Create(user, "vehicles", Values(10, SortDirection.Desc), Now), default);
        await repo.SaveGridAsync(GridSettings.Create(user, "expenses", Values(), Now), default);
        var replaced = (await repo.FindGridAsync(user, "vehicles", default))!;
        replaced.Update(Values(50, SortDirection.Desc), Now.AddMinutes(1));
        await repo.SaveGridAsync(replaced, default);

        var grids = await repo.ListGridsAsync(user, default);

        Assert.Equal(["expenses", "vehicles"], grids.Select(g => g.GridId));
        Assert.Equal(["name", "licensePlate"], grids[1].Order);
        Assert.Equal(["licensePlate"], grids[1].Hidden);
        Assert.Equal((50, SortDirection.Desc), (grids[1].PageSize, grids[1].SortDirection));
        Assert.Empty(await repo.ListGridsAsync(Guid.NewGuid(), default));
        Assert.True(await repo.RemoveGridAsync(user, "vehicles", default));
        Assert.False(await repo.RemoveGridAsync(user, "vehicles", default));
        Assert.Equal(["expenses"], (await repo.ListGridsAsync(user, default)).Select(g => g.GridId));

        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal("Asc", await ctx.Database.SqlQueryRaw<string>("SELECT SortDirection AS Value FROM GridSettings").SingleAsync()); // enum as text
    }

    [Fact]
    public async Task VehicleOrders_ReplaceLeavesOnlyTheNewRows_AndGoWithAPurgedVehicle()
    {
        await using var db = new TestDatabase();
        var vehicles = db.Get<IVehicleRepository>();
        var orders = db.Get<IVehicleOrderRepository>();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var a = TestData.Vehicle(alice, "A");
        var b = TestData.Vehicle(alice, "B");
        var c = TestData.Vehicle(alice, "C");
        foreach (var v in new[] { a, b, c }) await vehicles.AddAsync(v, default);
        await orders.ReplaceAsync(alice, [VehicleOrder.Create(alice, a.Id, 0), VehicleOrder.Create(alice, b.Id, 1)], default);
        await orders.ReplaceAsync(bob, [VehicleOrder.Create(bob, c.Id, 0)], default);

        await orders.ReplaceAsync(alice, [VehicleOrder.Create(alice, c.Id, 0), VehicleOrder.Create(alice, b.Id, 1)], default);

        Assert.Equal([c.Id, b.Id], (await orders.ListAsync(alice, default)).Select(o => o.VehicleId));
        Assert.Equal([c.Id], (await orders.ListAsync(bob, default)).Select(o => o.VehicleId)); // untouched

        var doomed = (await vehicles.FindAsync(c.Id, default))!;
        doomed.MarkDeleted(Now);
        await vehicles.UpdateAsync(doomed, default);
        await vehicles.PurgeAsync(OwnerScope.All, default);

        Assert.Equal([b.Id], (await orders.ListAsync(alice, default)).Select(o => o.VehicleId)); // the cascade took C's positions
        Assert.Empty(await orders.ListAsync(bob, default));
    }
}
