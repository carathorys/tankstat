using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Setup;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Instance;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The setup columns on a real database: decided once, completed once, the first administrator added only with the completion.</summary>
public class SetupStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ANewDatabase_IsDecidedOnce_AsWaiting_EvenWhenALaterStartSeesData()
    {
        await using var db = new TestDatabase();
        var store = db.Get<ISetupStore>();
        Assert.Equal(SetupPhase.Undecided, await store.PhaseAsync(default)); // no row yet

        Assert.Equal(SetupPhase.Pending, await store.DecideAsync(holdsData: false, Now, default));
        Assert.Equal(SetupPhase.Pending, await store.DecideAsync(holdsData: true, Now.AddMinutes(1), default)); // decided already: it stands

        await using var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var row = await context.InstanceState.SingleAsync();
        Assert.Equal((Now, (DateTimeOffset?)null, (SetupCompletion?)null), (row.SetupPendingSince!.Value, row.SetupCompletedAt, row.SetupCompletedVia));
    }

    [Fact]
    public async Task ADatabaseThatHoldsData_IsDecidedAsSetUp_ByTheUpgrade()
    {
        await using var db = new TestDatabase();
        var store = db.Get<ISetupStore>();

        Assert.Equal(SetupPhase.Completed, await store.DecideAsync(holdsData: true, Now, default));

        await using var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var row = await context.InstanceState.SingleAsync();
        Assert.Equal((Now, SetupCompletion.Upgrade), (row.SetupCompletedAt!.Value, row.SetupCompletedVia!.Value));
        Assert.Equal(SetupPhase.Completed, row.Setup);
    }

    [Fact]
    public async Task Data_IsAnyUser_OrAnyVehicle_TheTrashIncluded()
    {
        await using (var empty = new TestDatabase())
            Assert.False(await empty.Get<ISetupStore>().HoldsDataAsync(default));

        await using (var trashed = new TestDatabase())
        {
            var vehicles = trashed.Get<IVehicleRepository>();
            var car = Vehicle.Create(Guid.NewGuid(), "Old car", null, FuelType.Petrol, MeasurementUnits.Metric);
            await vehicles.AddAsync(car, default);
            car.MarkDeleted(Now);
            await vehicles.UpdateAsync(car, default);
            Assert.True(await trashed.Get<ISetupStore>().HoldsDataAsync(default));
        }

        await using var withUser = new TestDatabase();
        await withUser.Get<Tankstat.Application.Users.IUserRepository>().AddAsync(User.CreateLocal("someone@x.co", null, isAdmin: false), default);
        Assert.True(await withUser.Get<ISetupStore>().HoldsDataAsync(default));
    }

    [Fact]
    public async Task OnlyAWaitingSetup_IsCompleted_AndOnlyOnce()
    {
        await using var db = new TestDatabase();
        var store = db.Get<ISetupStore>();
        Assert.False(await store.CompleteAsync(SetupCompletion.Environment, Now, default)); // undecided: nothing to complete

        await store.DecideAsync(holdsData: false, Now, default);
        Assert.True(await store.CompleteAsync(SetupCompletion.Acknowledged, Now.AddMinutes(5), default));
        Assert.False(await store.CompleteAsync(SetupCompletion.Environment, Now.AddMinutes(6), default));

        await using var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var row = await context.InstanceState.SingleAsync();
        Assert.Equal((Now.AddMinutes(5), SetupCompletion.Acknowledged), (row.SetupCompletedAt!.Value, row.SetupCompletedVia!.Value));
    }

    [Fact]
    public async Task TheFirstAdministrator_IsAddedWithTheCompletion_AndOfTwoAtOnceOnlyOne()
    {
        await using var db = new TestDatabase();
        var store = db.Get<ISetupStore>();
        await store.DecideAsync(holdsData: false, Now, default);

        var results = await Task.WhenAll(
            store.CompleteWithAdministratorAsync(User.CreateLocal("first@x.co", "Administrator", isAdmin: true), Now, default),
            store.CompleteWithAdministratorAsync(User.CreateLocal("second@x.co", "Administrator", isAdmin: true), Now, default));

        Assert.Single(results, r => r);
        var users = await db.Get<Tankstat.Application.Users.IUserRepository>().ListAsync(default);
        Assert.True(Assert.Single(users).IsAdmin);
        Assert.Equal(SetupPhase.Completed, await store.PhaseAsync(default));
        Assert.False(await store.CompleteWithAdministratorAsync(User.CreateLocal("third@x.co", "Administrator", isAdmin: true), Now, default));
        Assert.Single(await db.Get<Tankstat.Application.Users.IUserRepository>().ListAsync(default));
    }
}
