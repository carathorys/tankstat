using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Access;
using Tankstat.Application.Photos;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

public class PhotoDraftRepositoryTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Vehicle> AddVehicle(TestDatabase db)
    {
        var v = TestData.Vehicle(Owner, "Car " + Guid.NewGuid().ToString("N")[..4]);
        await db.Get<IVehicleRepository>().AddAsync(v, default);
        return v;
    }

    private static PhotoDraft Draft(Vehicle v, Guid by, DateTimeOffset? at = null) => PhotoDraft.Create(Guid.NewGuid(), v.OwnerId, v.Id, by, at ?? Now);

    [Fact]
    public async Task Drafts_RoundTrip_AndAreCountedPerVehicleAndUploader()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var van = await AddVehicle(db);
        var repo = db.Get<IPhotoDraftRepository>();
        var mine = Draft(car, Alice);
        foreach (var d in new[] { mine, Draft(car, Alice), Draft(car, Bob), Draft(van, Alice) }) await repo.AddAsync(d, default);

        var loaded = (await repo.FindAsync(mine.Id, default))!;

        Assert.Equal((Owner, car.Id, Alice, Now), (loaded.OwnerId, loaded.VehicleId, loaded.CreatedById, loaded.CreatedAt));
        Assert.Equal(2, await repo.CountAsync(car.Id, Alice, default));
        Assert.Equal(1, await repo.CountAsync(car.Id, Bob, default));
        Assert.Null(await repo.FindAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task FindMany_AndRemove_WorkOnTheGivenIdsOnly()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var repo = db.Get<IPhotoDraftRepository>();
        var (one, two, three) = (Draft(car, Alice), Draft(car, Alice), Draft(car, Alice));
        foreach (var d in new[] { one, two, three }) await repo.AddAsync(d, default);

        var found = await repo.FindManyAsync([one.Id, three.Id, Guid.NewGuid()], default);
        await repo.RemoveAsync([one.Id, two.Id], default);

        Assert.Equal(new[] { one.Id, three.Id }.Order(), found.Select(d => d.Id).Order());
        Assert.Empty(await repo.FindManyAsync([], default));
        Assert.Equal([three.Id], (await repo.FindManyAsync([one.Id, two.Id, three.Id], default)).Select(d => d.Id));
    }

    [Fact]
    public async Task CreatedBefore_LooksAcrossAllVehiclesAndUploaders()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var van = await AddVehicle(db);
        var repo = db.Get<IPhotoDraftRepository>();
        var oldOnCar = Draft(car, Alice, Now.AddDays(-2));
        var oldOnVan = Draft(van, Bob, Now.AddHours(-25));
        var fresh = Draft(car, Alice, Now.AddHours(-1));
        foreach (var d in new[] { oldOnCar, oldOnVan, fresh }) await repo.AddAsync(d, default);

        var expired = await repo.ListCreatedBeforeAsync(Now.AddHours(-24), default);

        Assert.Equal(new[] { oldOnCar.Id, oldOnVan.Id }.Order(), expired.Select(d => d.Id).Order());
    }

    [Fact]
    public async Task PurgingAVehicle_RemovesItsDraftsInTheDatabase()
    {
        await using var db = new TestDatabase();
        var car = await AddVehicle(db);
        var van = await AddVehicle(db);
        var repo = db.Get<IPhotoDraftRepository>();
        await repo.AddAsync(Draft(car, Alice), default);
        var kept = Draft(van, Alice);
        await repo.AddAsync(kept, default);
        var vehicles = db.Get<IVehicleRepository>();
        var loaded = (await vehicles.FindAsync(car.Id, default))!;
        loaded.MarkDeleted(Now);
        await vehicles.UpdateAsync(loaded, default);

        await vehicles.PurgeAsync(OwnerScope.All, default);

        await using var ctx = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal([kept.Id], await ctx.PhotoDrafts.Select(d => d.Id).ToListAsync());
    }
}
