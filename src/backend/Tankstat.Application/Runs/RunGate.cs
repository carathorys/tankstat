using Microsoft.Extensions.Options;

namespace Tankstat.Application.Runs;

/// <summary>What this server last knew of open runs, so a save does not ask the database for marks every time (there is almost never one).</summary>
public sealed class RunGateState
{
    private long _noneOpenUntil; // ticks; 0: ask
    private int _lockedOut;

    public bool KnowsNoneOpen(DateTimeOffset now) => now.UtcTicks < Interlocked.Read(ref _noneOpenUntil);

    public void NoneOpen(DateTimeOffset until) => Interlocked.Exchange(ref _noneOpenUntil, until.UtcTicks);

    /// <summary>
    /// A run of this server holds the whole database's lock to write (SQLite, #335): nothing else can be saved until it ends. Work that
    /// writes on its own time (the photo worker, the scheduled backup) and reads that write on the side (the inbox's bring-up-to-date,
    /// the tombstone sweep) look here and come again later, the owner's decision 5; a person's save waits and is then refused as busy.
    /// </summary>
    public bool WritesLockedOut => Volatile.Read(ref _lockedOut) > 0;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IReadOnlySet<Guid>> _here = new();

    /// <summary>The runs open in this process: alive, however old their marks are.</summary>
    public bool IsOpenHere(Guid runId) => _here.ContainsKey(runId);

    /// <summary>Whether any run is open in this process.</summary>
    public bool AnyOpenHere => !_here.IsEmpty;

    /// <summary>
    /// Whether a run open in this process holds the car: until that run ends, whatever the clock says. Its limit is asked between its
    /// entries, so its last entry may cross it with the transaction still open.
    /// </summary>
    public bool HoldsHere(Guid vehicleId) => _here.Values.Any(vehicles => vehicles.Contains(vehicleId));

    /// <summary>A run opened in this process, with the cars it holds, from its mark until it ended.</summary>
    public void OpenedHere(Guid runId, IEnumerable<Guid> vehicleIds) => _here[runId] = vehicleIds.ToHashSet();

    public void EndedHere(Guid runId) => _here.TryRemove(runId, out _);

    public void LockedOut(bool entered) => _ = entered ? Interlocked.Increment(ref _lockedOut) : Interlocked.Decrement(ref _lockedOut);

    /// <summary>A mark was just written or ended on this server: the next save asks.</summary>
    public void Forget() => Interlocked.Exchange(ref _noneOpenUntil, 0);
}

/// <summary>
/// The gate every change to a car passes (#335, the owner's decision 1): while a run holds the car, someone else's save waits for it,
/// about ten seconds, and is then refused as busy. The run's own saves pass. Another server's mark is seen within
/// <see cref="RecheckAfter"/>, which the run waits out before it opens its transaction (<see cref="RunTimings.Settle"/>).
/// </summary>
public sealed class RunGate(IRunMarkerRepository markers, IOptionsMonitor<RunOptions> options, RunTimings timings, TimeProvider clock, RunGateState state)
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    /// <summary>Returns once no run holds the car; throws <see cref="BusyException"/> when one still does after the wait.</summary>
    public async Task WaitAsync(Guid vehicleId, CancellationToken ct)
    {
        if (RunContext.Current is not null) return; // the run's own saves
        var deadline = clock.GetUtcNow() + timings.BusyWait;
        while (await HeldAsync(vehicleId, ct))
        {
            if (clock.GetUtcNow() >= deadline) throw new BusyException();
            await Task.Delay(Poll, clock, ct);
        }
    }

    /// <summary>
    /// For a change that is not about one car (emptying a trash, deleting a user with their cars): waits while any run is open, since
    /// what it removes may be a run's car or one of its rows.
    /// </summary>
    public async Task WaitForAllAsync(CancellationToken ct)
    {
        if (RunContext.Current is not null) return;
        var deadline = clock.GetUtcNow() + timings.BusyWait;
        while (await AnyOpenAsync(ct))
        {
            if (clock.GetUtcNow() >= deadline) throw new BusyException();
            await Task.Delay(Poll, clock, ct);
        }
    }

    /// <summary>Whether a run is open on any server, as far as this one knows (asked at most once in <see cref="RecheckAfter"/> while none is).</summary>
    public async Task<bool> AnyOpenAsync(CancellationToken ct)
    {
        if (state.AnyOpenHere) return true; // this server's own run, alive: however old its mark is
        var now = clock.GetUtcNow();
        if (state.KnowsNoneOpen(now)) return false;
        if (await markers.AnyOpenAsync(now - options.CurrentValue.Limit, ct)) return true;
        state.NoneOpen(now + RecheckAfter);
        return false;
    }

    private async Task<bool> HeldAsync(Guid vehicleId, CancellationToken ct)
    {
        // This server's own run holds its cars until it ends: its last entry may cross the limit with the transaction still open.
        if (state.HoldsHere(vehicleId)) return true;
        var now = clock.GetUtcNow();
        if (state.KnowsNoneOpen(now)) return false;
        // Another server's mark older than the limit never blocks: its run is taken to be over, whatever the mark says (that run stops at
        // its next entry past the limit, counted from the mark's start; until then this server cannot know it is still alive).
        var since = now - options.CurrentValue.Limit;
        if (!await markers.AnyOpenAsync(since, ct))
        {
            state.NoneOpen(now + RecheckAfter);
            return false;
        }
        return await markers.HoldsAsync(vehicleId, since, ct);
    }
}
