using Tankstat.Application.Refuelings;
using Tankstat.Application.Sync;
using Tankstat.Domain;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Users;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

public class SyncServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    private sealed record Scene(World W, User Alice, User Bob, Vehicle Car);

    private static async Task<Scene> Setup()
    {
        var w = new World();
        var alice = w.AddUser("alice@x.co");
        var bob = w.AddUser("bob@x.co");
        w.Current.SignInAs(alice);
        var car = await w.VehicleService.AddAsync("Golf", null, FuelType.Petrol, default);
        return new Scene(w, alice, bob, car);
    }

    private static RefuelingInput Fill(long odometer, DateOnly? date = null) => new(date ?? Day, 40, 60, "EUR", odometer, true, null);

    /// <summary>A refuelling added on a device, as the API turns <c>logRefueling</c> into a request.</summary>
    private static SyncChangeRequest Log(Scene s, Guid id, long odometer, DateOnly? date = null) =>
        new(id, SyncChangeKind.LogRefueling, id, s.Car.Id, null, $"{{\"odometer\":{odometer}}}", async ct =>
        {
            var r = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(odometer, date), ct, id: id);
            return new AppliedChange(r.Id, r.Version, r.VehicleId);
        });

    private static SyncChangeRequest Update(Scene s, Guid changeId, Guid logId, int expectedVersion, long odometer) =>
        new(changeId, SyncChangeKind.UpdateRefueling, logId, null, expectedVersion, "{}", async ct =>
        {
            var r = await s.W.RefuelingService.UpdateAsync(logId, Fill(odometer), ct, expectedVersion);
            return new AppliedChange(r.Id, r.Version, r.VehicleId);
        }, async ct => (await s.W.Refuelings.FindIncludingDeletedAsync(logId, ct))?.VehicleId);

    [Fact]
    public async Task Changes_AreAppliedInOrder_AndRecorded_SoTheSameBatchAgainAppliesNothing()
    {
        var s = await Setup();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var batch = new[] { Log(s, a, 1000), Log(s, b, 1500, Day.AddDays(5)) };

        var first = await s.W.Sync.SyncAsync(batch, default);
        var again = await s.W.Sync.SyncAsync(batch, default);

        Assert.Equal(2, first.Applied);
        Assert.Equal([a, b], first.Results.Select(r => r.EntityId!.Value));
        Assert.Equal(first.Results, again.Results); // what happened the first time
        Assert.Equal(2, s.W.Refuelings.Items.Count); // and nothing twice
        Assert.All(s.W.SyncLedger.Items, c => Assert.Equal(s.Alice.Id, c.SubmittedById));
    }

    [Fact]
    public async Task WhatTheServerRefuses_IsParkedWithTheReason_AndTheRestGoesOn()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1000), default, id: Guid.NewGuid());
        await s.W.RefuelingService.UpdateAsync(log.Id, Fill(1100), default); // someone saved it meanwhile: version 2
        var (stale, below, fine) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var outcome = await s.W.Sync.SyncAsync([Update(s, stale, log.Id, 1, 1200), Log(s, below, 900, Day.AddDays(3)), Log(s, fine, 2000, Day.AddDays(4))], default);

        Assert.Equal((1, 2), (outcome.Applied, outcome.Parked));
        var parked = outcome.Results.Where(r => r.Status == SyncChangeStatus.Parked).ToList();
        Assert.Equal("sync.versionMismatch", parked[0].ReasonKey);
        Assert.Equal("1", parked[0].ReasonArgs["expected"]);
        Assert.StartsWith("odometer.", parked[1].ReasonKey);
        Assert.Equal(SyncChangeStatus.Applied, outcome.Results[2].Status);
        Assert.Equal(1100, (await s.W.RefuelingService.FindAsync(log.Id, default))!.OdometerReading!.Value); // the stale change did not overwrite
        var row = s.W.SyncLedger.Items.Single(c => c.Id == stale);
        Assert.Equal((SyncChangeStatus.Parked, s.Car.Id, s.Alice.Id), (row.Status, row.VehicleId!.Value, row.OwnerId));
    }

    [Fact]
    public async Task AChangeOfAVehicleThatIsNoMore_IsParkedWithTheSender_NotUnderTheVehicle()
    {
        var s = await Setup();
        var gone = Guid.NewGuid(); // purged meanwhile, or an add the server refused
        var change = Guid.NewGuid();
        var request = new SyncChangeRequest(change, SyncChangeKind.LogRefueling, Guid.NewGuid(), gone, null, "{}",
            _ => throw new NotFoundException("vehicle.notFound", "Vehicle not found.", new { Id = gone }));

        var outcome = await s.W.Sync.SyncAsync([request], default);

        Assert.Equal(SyncChangeStatus.Parked, Assert.Single(outcome.Results).Status);
        var row = s.W.SyncLedger.Items.Single(c => c.Id == change);
        Assert.Equal(((Guid?)null, s.Alice.Id), (row.VehicleId, row.OwnerId)); // no row under a vehicle that is not there (a foreign key)
    }

    [Fact]
    public async Task AChangeAnotherServerRecordedFirst_IsAnsweredWithWhatThatServerRecorded()
    {
        var s = await Setup();
        var id = Guid.NewGuid();
        var twin = SyncChange.Applied(id, s.Alice.Id, s.Alice.Id, s.Car.Id, id, SyncChangeKind.LogRefueling, null, "{}", DateTimeOffset.UtcNow, id, 1);
        s.W.SyncLedger.TwinRecordsNext = twin;

        var outcome = await s.W.Sync.SyncAsync([Log(s, id, 1000)], default);

        Assert.Equal(SyncChangeResult.From(twin), Assert.Single(outcome.Results));
    }

    [Fact]
    public async Task ABatchThatIsNotWellFormed_AppliesNothing()
    {
        var s = await Setup();
        var id = Guid.NewGuid();

        var twice = await Assert.ThrowsAsync<DomainException>(() => s.W.Sync.SyncAsync([Log(s, id, 1000), Log(s, id, 1100)], default));
        var none = await Assert.ThrowsAsync<DomainException>(() => s.W.Sync.SyncAsync([], default));
        var many = await Assert.ThrowsAsync<DomainException>(() => s.W.Sync.SyncAsync(Enumerable.Range(0, 201).Select(i => Log(s, Guid.NewGuid(), 1000 + i)).ToList(), default));

        Assert.Equal(("sync.duplicateChange", "sync.tooManyChanges", "sync.tooManyChanges"), (twice.Key, none.Key, many.Key));
        Assert.Empty(s.W.Refuelings.Items);
        Assert.Empty(s.W.SyncLedger.Items);
    }

    [Fact]
    public async Task AChangeIdSomeoneElseSent_IsRefused()
    {
        var s = await Setup();
        var id = Guid.NewGuid();
        await s.W.Sync.SyncAsync([Log(s, id, 1000)], default);

        s.W.Current.SignInAs(s.Bob);
        var e = await Assert.ThrowsAsync<DomainException>(() => s.W.Sync.SyncAsync([Log(s, id, 1000)], default));

        Assert.Equal("sync.idTaken", e.Key);
    }

    [Fact]
    public async Task AppliedRecords_GoAfterTheRetention_ParkedOnesStay()
    {
        var s = await Setup();
        var log = await s.W.RefuelingService.LogAsync(s.Car.Id, Fill(1000), default, id: Guid.NewGuid());
        await s.W.Sync.SyncAsync([Log(s, Guid.NewGuid(), 1100, Day.AddDays(1)), Update(s, Guid.NewGuid(), log.Id, 7, 1050)], default);

        s.W.Clock.Advance(TimeSpan.FromDays(31));
        await s.W.Sync.SyncAsync([Log(s, Guid.NewGuid(), 1200, Day.AddDays(2))], default);

        Assert.Equal([SyncChangeStatus.Parked, SyncChangeStatus.Applied], s.W.SyncLedger.Items.Select(c => c.Status)); // the old applied one went
    }
}
