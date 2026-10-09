using Tankstat.Domain;
using Tankstat.Domain.Sync;

namespace Tankstat.Domain.UnitTests;

/// <summary>The ledger row of a change a device sent: what it may hold, and that only a parked change can still be decided about.</summary>
public class SyncChangeTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static SyncChange Parked(string reason = "sync.versionMismatch", IReadOnlyDictionary<string, string>? args = null, string payload = "{}") =>
        SyncChange.Parked(Guid.NewGuid(), Owner, Owner, Guid.NewGuid(), Guid.NewGuid(), SyncChangeKind.UpdateRefueling, 3, payload, Now, reason, args ?? new Dictionary<string, string>());

    private static string Key(Action action) => Assert.Throws<DomainException>(action).Key;

    [Fact]
    public void AChange_NeedsAnId_AKnownKind_AndAPayloadThatFits()
    {
        Assert.Equal("id.empty", Key(() => SyncChange.Applied(Guid.Empty, Owner, Owner, null, null, SyncChangeKind.AddVehicle, null, "{}", Now, null, null)));
        Assert.Equal("sync.unknownKind", Key(() => SyncChange.Applied(Guid.NewGuid(), Owner, Owner, null, null, (SyncChangeKind)99, null, "{}", Now, null, null)));
        Assert.Equal("sync.payloadTooLarge", Key(() => Parked(payload: new string('x', SyncChange.MaxPayloadLength + 1))));
    }

    [Fact]
    public void AnAppliedChange_KeepsWhatItProduced_AndNoReason()
    {
        var resultId = Guid.NewGuid();

        var change = SyncChange.Applied(Guid.NewGuid(), Owner, Owner, null, resultId, SyncChangeKind.AddVehicle, null, "{}", Now, resultId, 1);

        Assert.Equal((SyncChangeStatus.Applied, resultId, (int?)1, (string?)null), (change.Status, change.ResultId, change.ResultVersion, change.ReasonKey));
        Assert.Empty(change.ReasonArgs);
    }

    [Fact]
    public void TheReason_IsCutToWhatTheLedgerHolds()
    {
        var args = Enumerable.Range(0, SyncChange.MaxReasonArgs + 5).ToDictionary(i => $"arg{i}", i => i == 0 ? new string('v', 300) : "short");

        var change = Parked(new string('k', SyncChange.MaxReasonKeyLength + 20), args);

        Assert.Equal(SyncChange.MaxReasonKeyLength, change.ReasonKey!.Length);
        Assert.Equal(SyncChange.MaxReasonArgs, change.ReasonArgs.Count);
        Assert.Equal(200, change.ReasonArgs["arg0"].Length);
        Assert.Equal("short", change.ReasonArgs["arg1"]);
    }

    [Fact]
    public void AParkedChange_CanBeEditedWithinTheLimit()
    {
        var change = Parked();

        change.Replace("""{"volume":40}""");

        Assert.Equal("""{"volume":40}""", change.Payload);
        Assert.Equal("sync.payloadTooLarge", Key(() => change.Replace(new string('x', SyncChange.MaxPayloadLength + 1))));
        Assert.Equal("""{"volume":40}""", change.Payload);
    }

    [Fact]
    public void ParkedAgain_ItTakesTheNewReason_AndIsFreeForTheNextDecision()
    {
        var change = Parked();

        change.ParkAgain("vehicle.notFound", new Dictionary<string, string> { ["id"] = "x" });

        Assert.Equal((SyncChangeStatus.Parked, "vehicle.notFound", (Guid?)null), (change.Status, change.ReasonKey, change.ResolvedById));
        Assert.Equal("x", change.ReasonArgs["id"]);
    }

    [Fact]
    public void OnceDecided_AChangeCannotBeDecidedAgain()
    {
        var applied = Parked();
        var discarded = Parked();
        applied.MarkApplied(Owner, Now, Guid.NewGuid(), 4);
        discarded.MarkDiscarded(Owner, Now);

        Assert.Equal((SyncChangeStatus.Applied, (Guid?)Owner, (DateTimeOffset?)Now, (string?)null), (applied.Status, applied.ResolvedById, applied.ResolvedAt, applied.ReasonKey));
        Assert.Equal(SyncChangeStatus.Discarded, discarded.Status);
        foreach (var decided in new[] { applied, discarded })
        {
            Assert.Equal("sync.notParked", Key(() => decided.MarkDiscarded(Owner, Now)));
            Assert.Equal("sync.notParked", Key(() => decided.MarkApplied(Owner, Now, null, null)));
            Assert.Equal("sync.notParked", Key(() => decided.ParkAgain("x", new Dictionary<string, string>())));
            Assert.Equal("sync.notParked", Key(() => decided.Replace("{}")));
        }
    }
}
