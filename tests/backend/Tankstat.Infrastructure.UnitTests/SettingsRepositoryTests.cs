using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public async Task UiSettings_ColorMode_RoundTrips_StoredByName()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUiSettingsRepository>();
        var user = Guid.NewGuid();
        var settings = UiSettings.Create(user, Now);
        settings.SetColorMode(ColorMode.System, Now);
        await repo.SaveAsync(settings, default);

        Assert.Equal(ColorMode.System, (await repo.FindAsync(user, default))!.ColorMode);
        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        // By name, like the other enums: a reordered enum never changes what a stored row means.
        Assert.Equal("System", await ctx.Database.SqlQueryRaw<string>("SELECT \"ColorMode\" AS \"Value\" FROM \"UiSettings\"").SingleAsync());
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

        async Task<Guid[]> Positions(Guid user)
        {
            await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await ctx.VehicleOrders.Where(o => o.UserId == user).OrderBy(o => o.Position).Select(o => o.VehicleId).ToArrayAsync();
        }
        Assert.Equal([c.Id, b.Id], await Positions(alice));
        Assert.Equal([c.Id], await Positions(bob)); // untouched

        var doomed = (await vehicles.FindAsync(c.Id, default))!;
        doomed.MarkDeleted(Now);
        await vehicles.UpdateAsync(doomed, default);
        await vehicles.PurgeAsync(OwnerScope.All, default);

        Assert.Equal([b.Id], await Positions(alice)); // the cascade took C's positions
        Assert.Empty(await Positions(bob));
    }

    [Fact]
    public async Task TwoFirstSaves_AtTheSameTime_BothSucceed_TheLaterOneWins()
    {
        await using var db = new TestDatabase();
        var repo = db.Get<IUiSettingsRepository>();
        var user = Guid.NewGuid();
        var first = UiSettings.Create(user, Now);
        first.SetNavOpen(false, Now);
        var second = UiSettings.Create(user, Now);
        second.SetLanguage("hu", Now.AddSeconds(1));
        await repo.SaveAsync(first, default); // the race as the service sees it: the second save was also built when no row existed ...

        await repo.SaveAsync(second, default); // ... and still saves: the repository finds the row by now and updates it

        var loaded = (await repo.FindAsync(user, default))!;
        Assert.Equal((null, "hu"), (loaded.NavOpen, loaded.Language)); // the later row as a whole: last write wins per row
    }

    [Fact]
    public async Task AFirstSave_ThatLosesTheInsertRace_IsRetriedAsAnUpdate()
    {
        var user = Guid.NewGuid();
        var rival = UiSettings.Create(user, Now);
        rival.SetNavOpen(false, Now);
        var race = new RivalInsert(rival);
        await using var db = new TestDatabase(services: s => s.ConfigureDbContext<AppDbContext>((_, o) => o.AddInterceptors(race)));
        race.Factory = db.Get<IDbContextFactory<AppDbContext>>();
        var repo = db.Get<IUiSettingsRepository>();
        var mine = UiSettings.Create(user, Now.AddSeconds(1));
        mine.SetLanguage("hu", Now.AddSeconds(1));

        await repo.SaveAsync(mine, default); // no row when it looked; the rival's insert lands in between

        Assert.Equal(1, race.Fired);
        var loaded = (await repo.FindAsync(user, default))!;
        Assert.Equal((null, "hu"), (loaded.NavOpen, loaded.Language)); // the insert failed and became an update of the whole row: the later save wins
    }

    /// <summary>Slips a row with the same key in between the repository's existence check and its INSERT: the race two first saves can run.</summary>
    private sealed class RivalInsert(UiSettings rival) : SaveChangesInterceptor
    {
        private bool _done;
        public IDbContextFactory<AppDbContext>? Factory { get; set; }
        public int Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done && Factory is not null && eventData.Context!.ChangeTracker.Entries<UiSettings>().Any(e => e.State == EntityState.Added))
            {
                _done = true; // the rival's own save goes through this interceptor too
                Fired++;
                await using var other = await Factory.CreateDbContextAsync(cancellationToken);
                other.UiSettings.Add(rival);
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }
}
