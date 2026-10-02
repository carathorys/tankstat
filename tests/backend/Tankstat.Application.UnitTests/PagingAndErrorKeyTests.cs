using Tankstat.Application;
using Tankstat.Application.Auth;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class PagingTests
{
    private static async Task<World> WorldWithVehicles(int count)
    {
        var w = new World(); // the paged list is an administrator feature
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));
        for (var i = 0; i < count; i++) await w.VehicleService.AddAsync($"Car {i:D3}", null, FuelType.Petrol, default);
        return w;
    }

    [Fact]
    public async Task List_PassesSortingAndPagingDown_AndCountsTheWholeList()
    {
        var w = await WorldWithVehicles(30);

        var page = await w.VehicleService.ListAsync(new VehicleQuery(VehicleSortField.Owner, SortDirection.Desc, 10, 5), default);

        Assert.Equal(new VehicleQuery(VehicleSortField.Owner, SortDirection.Desc, 10, 5), w.Vehicles.LastQuery);
        Assert.Equal(5, page.Count);
        Assert.Equal(30, await w.VehicleService.CountAsync(default));
    }

    [Theory]
    [InlineData(-5, 0, 0, 1)]
    [InlineData(10, 1000, 10, VehicleQuery.MaxTake)]
    [InlineData(0, 25, 0, 25)]
    public async Task OutOfRangePaging_IsClamped_NotRejected(int skip, int take, int expectedSkip, int expectedTake)
    {
        var w = await WorldWithVehicles(1);

        await w.VehicleService.ListAsync(new VehicleQuery(Skip: skip, Take: take), default);

        Assert.Equal((expectedSkip, expectedTake), (w.Vehicles.LastQuery!.Skip, w.Vehicles.LastQuery.Take));
    }

    [Fact]
    public async Task ThePagedList_IsForAdministratorsOnly_AndNeverAvailableWithoutAuthentication()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);
        await w.VehicleService.AddAsync("Mine", null, FuelType.Lpg, default);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.VehicleService.ListAsync(new VehicleQuery(), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.VehicleService.CountAsync(default));
        Assert.Single(await w.VehicleService.ListMineAsync(default)); // the home page still works

        var none = new World(AuthMode.None); // no real administrator exists then
        await Assert.ThrowsAsync<ForbiddenException>(() => none.VehicleService.ListAsync(new VehicleQuery(), default));
    }

    [Fact]
    public async Task TheHomeList_IsPagedAndSearchable_ForEveryoneWithTheirOwnVehiclesOnly()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        foreach (var name in new[] { "Golf", "Octavia", "Polo", "Passat" }) await w.VehicleService.AddAsync(name, name == "Polo" ? "xy-123" : null, FuelType.Lpg, default);
        w.Current.SignInAs(bob);
        await w.VehicleService.AddAsync("Polo of Bob", null, FuelType.Lpg, default);
        w.Current.SignInAs(alice);

        Assert.Equal(["Golf", "Octavia"], (await w.VehicleService.ListMineAsync(null, 0, 2, default)).Select(v => v.Name));
        Assert.Equal(["Passat", "Polo"], (await w.VehicleService.ListMineAsync(null, 2, 2, default)).Select(v => v.Name));
        Assert.Equal(["Polo"], (await w.VehicleService.ListMineAsync("XY", 0, 10, default)).Select(v => v.Name)); // by plate, ignoring case
        Assert.Equal(4, await w.VehicleService.CountMineAsync(null, default)); // Bob's car is not hers
        Assert.Equal(3, await w.VehicleService.CountMineAsync("o", default)); // Golf, Octavia, Polo (Bob's "Polo of Bob" is not hers)
    }

    [Fact]
    public async Task Counts_FollowTheAccessScope()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        for (var i = 0; i < 3; i++) await w.VehicleService.AddAsync($"A{i}", null, FuelType.Lpg, default);
        w.Current.SignInAs(bob);
        await w.VehicleService.AddAsync("B", null, FuelType.Lpg, default);

        Assert.Single(await w.VehicleService.ListMineAsync(default));
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.View));
        Assert.Equal(4, (await w.VehicleService.ListMineAsync(default)).Count);
    }

    [Fact]
    public async Task TrashCount_OnlyCountsWhatTheUserMayRestore()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Lpg, default);
        await w.VehicleService.DeleteAsync(car.Id, default);
        Assert.Equal(1, await w.VehicleService.CountTrashAsync(default));

        w.Current.SignInAs(bob);
        Assert.Equal(0, await w.VehicleService.CountTrashAsync(default));
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.View));
        Assert.Equal(0, await w.VehicleService.CountTrashAsync(default)); // view is not enough
        w.Grants.Items.Clear();
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.Edit));
        Assert.Equal(1, await w.VehicleService.CountTrashAsync(default));
    }

    [Fact]
    public async Task RefuelingCount_IsZeroForVehiclesTheUserCannotSee()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Lpg, default);
        await w.RefuelingService.LogAsync(car.Id, new(2026, 10, 1), 10, 10, 10, true, default);
        Assert.Equal(1, await w.RefuelingService.CountAsync(car.Id, default));

        w.Current.SignInAs(bob);
        Assert.Equal(0, await w.RefuelingService.CountAsync(car.Id, default));
    }
}

public class ApplicationErrorKeyTests
{
    [Fact]
    public async Task NotFound_And_Forbidden_CarKeysAndArguments()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Car", null, FuelType.Lpg, default);
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.View));

        var missing = await Assert.ThrowsAsync<NotFoundException>(() => w.VehicleService.DeleteAsync(Guid.NewGuid(), default));
        Assert.Equal("vehicle.notFound", missing.Key);
        Assert.True(missing.Args.ContainsKey("id"));

        w.Current.SignInAs(bob);
        Assert.Equal("vehicle.viewOnly", (await Assert.ThrowsAsync<ForbiddenException>(() => w.VehicleService.DeleteAsync(car.Id, default))).Key);
    }

    [Fact]
    public async Task Auth_Errors()
    {
        var w = new World();
        var user = w.AddUser("alice@x.co");

        Assert.Equal("auth.unauthenticated", (await Assert.ThrowsAsync<UnauthenticatedException>(() => w.Access.RequirePrincipalAsync(default))).Key);
        Assert.Equal("auth.invalidCredentials", (await Assert.ThrowsAsync<InvalidCredentialsException>(() => w.Auth.LoginAsync("alice@x.co", "wrong", default))).Key);
        w.Current.SignInAs(user);
        Assert.Equal("auth.adminRequired", (await Assert.ThrowsAsync<ForbiddenException>(() => w.Access.RequireAdminAsync(default))).Key);

        var oidc = new World(AuthMode.Oidc);
        var mode = await Assert.ThrowsAsync<DomainException>(() => oidc.Auth.LoginAsync("a@x.co", "x", default));
        Assert.Equal("auth.modeUnavailable", mode.Key);
        Assert.Equal("Oidc", mode.Args["mode"]);
    }

    [Fact]
    public async Task User_Admin_And_Password_Errors()
    {
        var w = new World();
        var admin = w.AddUser("root@x.co", admin: true);
        w.AddUser("taken@x.co");
        w.Current.SignInAs(admin);

        Assert.Equal("user.emailExists", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.CreateLocalAsync("taken@x.co", null, false, default))).Key);
        Assert.Equal("user.cannotDemoteSelf", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetAdminAsync(admin.Id, false, default))).Key);
        Assert.Equal("user.cannotDisableSelf", (await Assert.ThrowsAsync<DomainException>(() => w.UserService.SetDisabledAsync(admin.Id, true, default))).Key);
        Assert.Equal("user.notFound", (await Assert.ThrowsAsync<NotFoundException>(() => w.UserService.SetAdminAsync(Guid.NewGuid(), true, default))).Key);
        Assert.Equal("password.currentIncorrect", (await Assert.ThrowsAsync<DomainException>(() => w.Auth.ChangePasswordAsync("wrong", "new-password-123", default))).Key);
        Assert.Equal("reset.invalid", (await Assert.ThrowsAsync<DomainException>(() => w.Auth.ResetPasswordAsync("garbage", "new-password-123", default))).Key);
    }
}
