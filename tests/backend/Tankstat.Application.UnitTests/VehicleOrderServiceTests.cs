using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class VehicleOrderServiceTests
{
    private sealed record Scene(World W, User Alice, User Bob, Vehicle Alpha, Vehicle Beta, Vehicle Gamma, Vehicle Bobs);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(bob);
        var bobs = await w.VehicleService.AddAsync("Bobs", null, FuelType.Petrol, default);
        w.Current.SignInAs(alice);
        var alpha = await w.VehicleService.AddAsync("Alpha", null, FuelType.Petrol, default);
        var beta = await w.VehicleService.AddAsync("Beta", null, FuelType.Petrol, default);
        var gamma = await w.VehicleService.AddAsync("Gamma", null, FuelType.Petrol, default);
        return new Scene(w, alice, bob, alpha, beta, gamma, bobs);
    }

    private static async Task<(Guid VehicleId, int Position)[]> Rows(Scene s) =>
        (await s.W.VehicleOrders.ListAsync(s.Alice.Id, default)).Select(o => (o.VehicleId, o.Position)).ToArray();

    [Fact]
    public async Task Set_ReplacesTheWholeOrder_DropsDuplicates_AndNumbersFromZero()
    {
        var s = await Setup();

        await s.W.VehicleOrder.SetAsync([s.Gamma.Id, s.Alpha.Id], default);
        await s.W.VehicleOrder.SetAsync([s.Beta.Id, s.Gamma.Id, s.Beta.Id], default);

        Assert.Equal([(s.Beta.Id, 0), (s.Gamma.Id, 1)], await Rows(s));
    }

    [Fact]
    public async Task Set_AcceptsASharedVehicleTheUserMaySee()
    {
        var s = await Setup();
        s.W.ResourceGrants.Items.Add(ResourceGrant.Create(ResourceType.Vehicle, s.Bobs.Id, s.Alice.Id, GrantedFeature.Logs, AccessLevel.Edit));

        await s.W.VehicleOrder.SetAsync([s.Bobs.Id, s.Alpha.Id], default);

        Assert.Equal([(s.Bobs.Id, 0), (s.Alpha.Id, 1)], await Rows(s));
    }

    [Fact]
    public async Task Set_RefusesVehiclesTheUserMayNotSee_AndSavesNothing()
    {
        var s = await Setup();

        var error = await Assert.ThrowsAsync<NotFoundException>(() => s.W.VehicleOrder.SetAsync([s.Alpha.Id, s.Bobs.Id], default));

        Assert.Equal("vehicle.notFound", error.Key);
        Assert.Equal(s.Bobs.Id, error.Args["id"]);
        Assert.Empty(await Rows(s));
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.VehicleOrder.SetAsync([Guid.NewGuid()], default));
        await s.W.VehicleService.DeleteAsync(s.Beta.Id, default); // in the trash: invisible
        await Assert.ThrowsAsync<NotFoundException>(() => s.W.VehicleOrder.SetAsync([s.Beta.Id], default));
    }

    [Fact]
    public async Task Set_RefusesMoreThanTheMaximum()
    {
        var s = await Setup();

        var error = await Assert.ThrowsAsync<DomainException>(() => s.W.VehicleOrder.SetAsync(Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList(), default));

        Assert.Equal(("vehicleOrder.tooMany", 200), (error.Key, error.Args["max"]));
    }

    [Fact]
    public async Task TheHomeList_IsAskedForInTheUsersOrder_AndLogsOnlyIds()
    {
        var s = await Setup();

        await s.W.VehicleService.ListMineAsync(null, 0, 10, default);
        await s.W.VehicleOrder.SetAsync([s.Gamma.Id], default);

        Assert.Equal(s.Alice.Id, s.W.Vehicles.LastQuery!.OrderedFor);
        var line = Assert.Single(s.W.Log.From<Application.Settings.VehicleOrderService>());
        Assert.Equal((s.Alice.Id, 1), (line.Values["UserId"], line.Values["Count"]));
    }
}
