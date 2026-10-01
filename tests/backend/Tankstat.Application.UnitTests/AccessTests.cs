using Tankstat.Application.Vehicles;
using Tankstat.Application;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class AccessServiceTests
{
    [Fact]
    public async Task NoAuth_ActsAsAnonymousWithFullAccess()
    {
        var w = new World(AuthMode.None);

        Assert.True((await w.Access.RequirePrincipalAsync(default)).IsAnonymous);
        Assert.True((await w.Access.ScopeAsync(AccessLevel.Edit, default)).IsAll);
    }

    [Fact]
    public async Task NoAuth_HasNoAdministratorFeatures()
    {
        var w = new World(AuthMode.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Access.RequireAdminAsync(default));
    }

    [Fact]
    public async Task Auth_WithoutSignIn_IsUnauthenticated()
    {
        var w = new World();

        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.Access.RequirePrincipalAsync(default));
    }

    [Fact]
    public async Task Scope_IsOnlyOwnData_ByDefault()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        w.Current.SignInAs(alice);

        var scope = await w.Access.ScopeAsync(AccessLevel.View, default);

        Assert.False(scope.IsAll);
        Assert.Equal([alice.Id], scope.Owners);
    }

    [Fact]
    public async Task Scope_IncludesGrantedOwners_ByLevel()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        var carol = w.AddUser("carol@x.co");
        w.Grants.Items.Add(AccessGrant.Create(bob.Id, alice.Id, AccessLevel.View));
        w.Grants.Items.Add(AccessGrant.Create(carol.Id, alice.Id, AccessLevel.Edit));
        w.Current.SignInAs(alice);

        var view = await w.Access.ScopeAsync(AccessLevel.View, default);
        var edit = await w.Access.ScopeAsync(AccessLevel.Edit, default);

        Assert.Equivalent(new[] { alice.Id, bob.Id, carol.Id }, view.Owners);
        Assert.Equivalent(new[] { alice.Id, carol.Id }, edit.Owners);
    }

    [Fact]
    public async Task Scope_IsEverything_WhenInstanceDefaultAllows()
    {
        var w = new World();
        w.Settings.Value.SetDefaultLevelForOthers(AccessLevel.View);
        w.Current.SignInAs(w.AddUser("alice@x.co"));

        Assert.True((await w.Access.ScopeAsync(AccessLevel.View, default)).IsAll);
        Assert.False((await w.Access.ScopeAsync(AccessLevel.Edit, default)).IsAll);
    }

    [Fact]
    public async Task Admin_SeesEverything()
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));

        Assert.True((await w.Access.ScopeAsync(AccessLevel.Edit, default)).IsAll);
    }
}

public class VehicleAccessTests
{
    private static async Task<(World W, User Alice, User Bob, Vehicle AlicesCar)> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Alice car", null, FuelType.Petrol, default);
        w.Current.SignInAs(bob);
        return (w, alice, bob, car);
    }

    [Fact]
    public async Task Add_AssignsTheCurrentUserAsOwner()
    {
        var (w, alice, _, car) = await Setup();

        Assert.Equal(alice.Id, car.OwnerId);
        Assert.Same(car, Assert.Single(w.Vehicles.Items));
    }

    [Fact]
    public async Task OthersCannotSeeAVehicle_ByDefault()
    {
        var (w, _, _, car) = await Setup();

        Assert.Empty(await w.VehicleService.ListAsync(new VehicleQuery(), default));
        Assert.Null(await w.VehicleService.FindAsync(car.Id, default));
    }

    [Fact]
    public async Task ViewGrant_AllowsReading_ButNotLogging()
    {
        var (w, alice, bob, car) = await Setup();
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.View));

        Assert.Single(await w.VehicleService.ListAsync(new VehicleQuery(), default));
        Assert.NotNull(await w.VehicleService.FindAsync(car.Id, default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.RefuelingService.LogAsync(car.Id, new(2026, 10, 1), 10, 10, 10, true, default));
    }

    [Fact]
    public async Task EditGrant_AllowsLogging_AndRefuelingBelongsToTheVehicleOwner()
    {
        var (w, alice, bob, car) = await Setup();
        w.Grants.Items.Add(AccessGrant.Create(alice.Id, bob.Id, AccessLevel.Edit));

        var refueling = await w.RefuelingService.LogAsync(car.Id, new(2026, 10, 1), 10, 10, 10, true, default);

        Assert.Equal(alice.Id, refueling.OwnerId);
    }

    [Fact]
    public async Task LoggingOnAnInvisibleVehicle_LooksLikeItDoesNotExist()
    {
        var (w, _, _, car) = await Setup();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            w.RefuelingService.LogAsync(car.Id, new(2026, 10, 1), 10, 10, 10, true, default));
        Assert.Empty(await w.RefuelingService.ListForVehicleAsync(car.Id, default));
    }

    [Fact]
    public async Task LoggingOnAMissingVehicle_IsNotFound()
    {
        var (w, _, _, _) = await Setup();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            w.RefuelingService.LogAsync(Guid.NewGuid(), new(2026, 10, 1), 10, 10, 10, true, default));
    }

    [Fact]
    public async Task Unauthenticated_CannotListOrAdd()
    {
        var (w, _, _, _) = await Setup();
        w.Current.Principal = null;

        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.VehicleService.ListAsync(new VehicleQuery(), default));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => w.VehicleService.AddAsync("x", null, FuelType.Diesel, default));
    }

    [Fact]
    public async Task NoAuthMode_EveryoneSeesEverything()
    {
        var w = new World(AuthMode.None);
        await w.VehicleService.AddAsync("Shared", null, FuelType.Lpg, default);

        Assert.Single(await w.VehicleService.ListAsync(new VehicleQuery(), default));
    }
}

public class AccessAdminTests
{
    [Fact]
    public async Task OnlyAdministrators_CanChangeAccessRules()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.AccessAdmin.SetDefaultLevelAsync(AccessLevel.Edit, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.AccessAdmin.SetGrantAsync(bob.Id, alice.Id, AccessLevel.View, default));
    }

    [Fact]
    public async Task Administrator_SetsDefault_AndManagesGrants()
    {
        var w = new World();
        var admin = w.AddUser("root@x.co", admin: true);
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(admin);

        await w.AccessAdmin.SetDefaultLevelAsync(AccessLevel.View, default);
        await w.AccessAdmin.SetGrantAsync(alice.Id, bob.Id, AccessLevel.View, default);
        await w.AccessAdmin.SetGrantAsync(alice.Id, bob.Id, AccessLevel.Edit, default); // upgrade, not duplicate

        Assert.Equal(AccessLevel.View, w.Settings.Value.DefaultLevelForOthers);
        Assert.Equal(AccessLevel.Edit, Assert.Single(await w.AccessAdmin.ListGrantsAsync(default)).Level);

        await w.AccessAdmin.SetGrantAsync(alice.Id, bob.Id, AccessLevel.None, default); // removes
        Assert.Empty(await w.AccessAdmin.ListGrantsAsync(default));
    }

    [Fact]
    public async Task Grant_ForUnknownUser_IsNotFound()
    {
        var w = new World();
        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            w.AccessAdmin.SetGrantAsync(admin.Id, Guid.NewGuid(), AccessLevel.View, default));
    }
}
