using Tankstat.Application.Vehicles;
using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class VehicleLifecycleTests
{
    private static async Task<(World W, User Alice, User Bob, Vehicle Car)> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Alice car", "ab-1", FuelType.Petrol, default);
        return (w, alice, bob, car);
    }

    [Fact]
    public async Task Owner_CanEditAVehicle()
    {
        var (w, _, _, car) = await Setup();

        var updated = await w.VehicleService.UpdateAsync(car.Id, "Renamed", null, FuelType.Diesel, default);

        Assert.Equal(("Renamed", FuelType.Diesel), (updated.Name, updated.FuelType));
    }

    [Fact]
    public async Task Edit_WithInvalidValues_IsRejected()
    {
        var (w, _, _, car) = await Setup();

        await Assert.ThrowsAsync<DomainException>(() => w.VehicleService.UpdateAsync(car.Id, " ", null, FuelType.Diesel, default));
        Assert.Equal("Alice car", car.Name);
    }

    [Fact]
    public async Task Delete_MovesToTrash_Timestamped_AndHidesFromTheList()
    {
        var (w, _, _, car) = await Setup();
        w.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero));

        await w.VehicleService.DeleteAsync(car.Id, default);

        Assert.Equal(w.Clock.GetUtcNow(), car.DeletedAt);
        Assert.Empty(await w.VehicleService.ListAsync(new VehicleQuery(), default));
        Assert.Null(await w.VehicleService.FindAsync(car.Id, default));
        Assert.Same(car, Assert.Single(await w.VehicleService.ListTrashAsync(new VehicleQuery(), default)));
        Assert.Single(w.Vehicles.Items); // logical deletion only: the row is still there
    }

    [Fact]
    public async Task Restore_BringsTheVehicleBack()
    {
        var (w, _, _, car) = await Setup();
        await w.VehicleService.DeleteAsync(car.Id, default);

        await w.VehicleService.RestoreAsync(car.Id, default);

        Assert.Single(await w.VehicleService.ListAsync(new VehicleQuery(), default));
        Assert.Empty(await w.VehicleService.ListTrashAsync(new VehicleQuery(), default));
    }

    [Fact]
    public async Task TrashedVehicle_CannotBeEditedDeletedAgainOrLoggedAgainst()
    {
        var (w, _, _, car) = await Setup();
        await w.VehicleService.DeleteAsync(car.Id, default);

        await Assert.ThrowsAsync<NotFoundException>(() => w.VehicleService.UpdateAsync(car.Id, "x", null, FuelType.Lpg, default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.VehicleService.DeleteAsync(car.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => w.RefuelingService.LogAsync(car.Id, new(2026, 10, 1), 1, 1, 1, true, default));
    }

    [Fact]
    public async Task EmptyTrash_RemovesOnlyTrashedVehicles_AndReportsTheCount()
    {
        var (w, _, _, car) = await Setup();
        var keep = await w.VehicleService.AddAsync("Keep", null, FuelType.Lpg, default);
        var other = await w.VehicleService.AddAsync("Gone too", null, FuelType.Lpg, default);
        await w.VehicleService.DeleteAsync(car.Id, default);
        await w.VehicleService.DeleteAsync(other.Id, default);

        var removed = await w.VehicleService.EmptyTrashAsync(default);

        Assert.Equal(2, removed);
        Assert.Same(keep, Assert.Single(w.Vehicles.Items));
        Assert.Equal(0, await w.VehicleService.EmptyTrashAsync(default));
    }

    [Fact]
    public async Task OtherUsers_CannotChangeOrSeeTheTrashOfAVehicleTheyCannotAccess()
    {
        var (w, _, bob, car) = await Setup();
        await w.VehicleService.DeleteAsync(car.Id, default);
        w.Current.SignInAs(bob);

        await Assert.ThrowsAsync<NotFoundException>(() => w.VehicleService.RestoreAsync(car.Id, default));
        Assert.Empty(await w.VehicleService.ListTrashAsync(new VehicleQuery(), default));
        Assert.Equal(0, await w.VehicleService.EmptyTrashAsync(default));
        Assert.Single(w.Vehicles.Items);
    }

    [Fact]
    public async Task ViewGrant_CannotEditOrDelete_AndSeesNoTrash()
    {
        var (w, alice, bob, car) = await Setup();
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.View));
        w.Current.SignInAs(bob);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.VehicleService.UpdateAsync(car.Id, "x", null, FuelType.Lpg, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.VehicleService.DeleteAsync(car.Id, default));

        w.Current.SignInAs(alice);
        await w.VehicleService.DeleteAsync(car.Id, default);
        w.Current.SignInAs(bob);
        Assert.Empty(await w.VehicleService.ListTrashAsync(new VehicleQuery(), default));
        Assert.Equal(0, await w.VehicleService.EmptyTrashAsync(default));
    }

    [Fact]
    public async Task EditGrant_CanDeleteRestoreAndEmptyTheTrash_ForThatOwnersVehiclesOnly()
    {
        var (w, alice, bob, car) = await Setup();
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.Edit));
        w.Current.SignInAs(bob);
        var bobs = await w.VehicleService.AddAsync("Bob car", null, FuelType.Diesel, default);

        await w.VehicleService.DeleteAsync(car.Id, default); // Alice's, via the grant
        await w.VehicleService.DeleteAsync(bobs.Id, default);
        Assert.Equal(2, (await w.VehicleService.ListTrashAsync(new VehicleQuery(), default)).Count);
        await w.VehicleService.RestoreAsync(car.Id, default);

        Assert.Equal(1, await w.VehicleService.EmptyTrashAsync(default));
        Assert.DoesNotContain(bobs, w.Vehicles.Items);
        Assert.Contains(car, w.Vehicles.Items);
    }

    [Fact]
    public async Task Administrator_EmptiesEveryonesTrash()
    {
        var (w, _, _, car) = await Setup();
        await w.VehicleService.DeleteAsync(car.Id, default);
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));

        Assert.Single(await w.VehicleService.ListTrashAsync(new VehicleQuery(), default));
        Assert.Equal(1, await w.VehicleService.EmptyTrashAsync(default));
    }

    [Fact]
    public async Task NoAuth_EveryoneCanDeleteAndRestore()
    {
        var w = new World(AuthMode.None);
        var car = await w.VehicleService.AddAsync("Shared", null, FuelType.Lpg, default);

        await w.VehicleService.DeleteAsync(car.Id, default);
        await w.VehicleService.RestoreAsync(car.Id, default);

        Assert.Single(await w.VehicleService.ListAsync(new VehicleQuery(), default));
    }

    [Fact]
    public async Task Unauthenticated_CannotUseTheTrash()
    {
        var (w, _, _, car) = await Setup();
        w.Current.Principal = null;

        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.VehicleService.DeleteAsync(car.Id, default));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.VehicleService.ListTrashAsync(new VehicleQuery(), default));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.VehicleService.EmptyTrashAsync(default));
    }
}
