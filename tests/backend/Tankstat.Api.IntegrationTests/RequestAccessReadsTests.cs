using Microsoft.AspNetCore.Http;
using Tankstat.Api.Auth;
using Tankstat.Application.Access;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The request's copy of the access tables (#320), directly: fields asking at once share one read, a write's clear makes the next ask read
/// again, every user and table has its own value, and without a request nothing is kept.
/// </summary>
public class RequestAccessReadsTests
{
    private static (RequestAccessReads Reads, HttpContextAccessor Accessor) InARequest() =>
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() } is var accessor ? (new RequestAccessReads(accessor), accessor) : default;

    [Fact]
    public async Task FieldsAskingAtOnce_ShareOneRead_UntilAWriteClearsIt()
    {
        var (reads, _) = InARequest();
        var calls = 0;
        var gate = new TaskCompletionSource();
        async Task<int> Read(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await gate.Task; // still reading while the others ask
            return 42;
        }
        var key = new AccessReadKey(AccessRead.Grants, Guid.NewGuid());

        var asks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => reads.GetAsync(key, Read, default))).ToList();
        await Task.Delay(50);
        gate.SetResult();
        Assert.All(await Task.WhenAll(asks), v => Assert.Equal(42, v));
        Assert.Equal(1, calls);

        reads.Clear();
        Assert.Equal(42, await reads.GetAsync(key, Read, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task EachUserAndTable_HasItsOwnValue_AndWithoutARequestEveryAskReads()
    {
        var (reads, _) = InARequest();
        var (alice, bob) = (Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal("alice", await reads.GetAsync(new AccessReadKey(AccessRead.Grants, alice), _ => Task.FromResult("alice"), default));
        Assert.Equal("bob", await reads.GetAsync(new AccessReadKey(AccessRead.Grants, bob), _ => Task.FromResult("bob"), default));
        Assert.Equal("logs", await reads.GetAsync(new AccessReadKey(AccessRead.LogGrants, alice), _ => Task.FromResult("logs"), default));
        Assert.Equal("alice", await reads.GetAsync(new AccessReadKey(AccessRead.Grants, alice), _ => Task.FromResult("again"), default));

        // The accessor keeps the request in one async-local for every instance: outside a request it holds none.
        var outside = new RequestAccessReads(new HttpContextAccessor { HttpContext = null });
        var calls = 0;
        for (var i = 0; i < 3; i++) await outside.GetAsync(new AccessReadKey(AccessRead.Settings, Guid.Empty), _ => Task.FromResult(++calls), default);
        outside.Clear();
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task AFailedRead_IsEveryWaitingAsksFailure()
    {
        var (reads, _) = InARequest();
        var key = new AccessReadKey(AccessRead.Settings, Guid.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reads.GetAsync<int>(key, _ => throw new InvalidOperationException("the database is away"), default));
    }
}
