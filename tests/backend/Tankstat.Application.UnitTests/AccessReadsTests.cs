using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Runs;
using Tankstat.Application.Sharing;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// The access tables read once per request (#320): <see cref="AccessService"/> reads the settings, the user's grants and their log grants
/// through <see cref="IAccessReads"/>, every answer as before; a run reads the tables itself; a write to them clears what was kept.
/// </summary>
public class AccessReadsTests
{
    /// <summary>The request's copy as the Api keeps it: each key read once, until cleared. What was read, and how often it was cleared.</summary>
    private sealed class KeepingReads : IAccessReads
    {
        private readonly Dictionary<AccessReadKey, object> _kept = [];
        public List<AccessReadKey> Read { get; } = [];
        public int Cleared { get; private set; }

        public Task<T> GetAsync<T>(AccessReadKey key, Func<CancellationToken, Task<T>> read, CancellationToken ct)
        {
            if (_kept.TryGetValue(key, out var kept)) return (Task<T>)kept;
            Read.Add(key);
            var task = read(ct);
            _kept[key] = task;
            return task;
        }

        public void Clear()
        {
            _kept.Clear();
            Cleared++;
        }
    }

    /// <summary>Alice owns the Golf, Carol the Fiat; Bob holds a View grant on Alice's data and an Edit grant on the Fiat's logs.</summary>
    private static async Task<(World W, Vehicle Golf, Vehicle Fiat, Guid Alice, Guid Carol)> SharedAsync()
    {
        var w = new World();
        var (alice, bob, carol, admin) = (w.AddUser("alice@x.co"), w.AddUser("bob@x.co"), w.AddUser("carol@x.co"), w.AddUser("root@x.co", admin: true));
        w.Current.SignInAs(alice);
        var golf = await w.VehicleService.AddAsync("Golf", null, FuelType.Petrol, default);
        w.Current.SignInAs(carol);
        var fiat = await w.VehicleService.AddAsync("Fiat", null, FuelType.Petrol, default);
        await w.Sharing.SetLogAccessAsync(fiat.Id, bob.Id, AccessLevel.Edit, default);
        w.Current.SignInAs(admin);
        await w.AccessAdmin.SetGrantAsync(alice.Id, bob.Id, AccessLevel.View, default);
        w.Current.SignInAs(bob);
        return (w, (await w.Vehicles.FindAsync(golf.Id, default))!, (await w.Vehicles.FindAsync(fiat.Id, default))!, alice.Id, carol.Id);
    }

    private static AccessService Keeping(World w, KeepingReads reads) => new(w.Current, w.Options.Create(), w.Grants, w.Settings, w.ResourceGrants, reads: reads);

    /// <summary>Every question the home page and the log lists ask, so the two services can be compared answer by answer.</summary>
    private static async Task<string> AnswersAsync(AccessService access, Vehicle golf, Vehicle fiat, Guid alice, Guid carol)
    {
        var scope = await access.ScopeAsync(AccessLevel.View, default);
        var vehicles = await access.VehicleScopeAsync(AccessLevel.View, default);
        var logs = await access.LogScopeAsync(AccessLevel.Edit, default);
        var levels = await access.LevelsAsync([alice, carol], default);
        var logLevels = await access.LogLevelsAsync([golf, fiat], default);
        return string.Join(" | ",
            $"{scope.Contains(alice)} {scope.Contains(carol)}",
            $"{vehicles.Contains(carol, fiat.Id)} {vehicles.Contains(carol, Guid.NewGuid())}",
            $"{logs.Contains(carol, fiat.Id)} {logs.Contains(alice, golf.Id)}",
            $"{levels[alice]} {levels[carol]}", $"{logLevels[golf.Id]} {logLevels[fiat.Id]}",
            await access.LevelAsync(alice, default), await access.LogLevelAsync(golf, default), await access.LogLevelAsync(fiat, default),
            await access.VehicleLevelAsync(fiat, default));
    }

    [Fact]
    public async Task ManyQuestions_ReadEachAccessTableOnce_AndAnswerAsBefore()
    {
        var (w, golf, fiat, alice, carol) = await SharedAsync();
        var reads = new KeepingReads();

        var kept = await AnswersAsync(Keeping(w, reads), golf, fiat, alice, carol);

        Assert.Equal(await AnswersAsync(w.Access, golf, fiat, alice, carol), kept);
        Assert.Equal("True False | True False | True False | View None | View Edit | View | View | Edit | View", kept);
        var bob = (await w.Access.RequirePrincipalAsync(default)).Id;
        Assert.Equal([new(AccessRead.Settings, Guid.Empty), new(AccessRead.Grants, bob), new(AccessRead.LogGrants, bob)], reads.Read.OrderBy(k => k.Read));
    }

    [Fact]
    public async Task TheOwnerTheAdministratorAndARaisedDefault_AnswerAsBefore_AndTheAdministratorReadsNoTable()
    {
        var (w, golf, fiat, alice, carol) = await SharedAsync();
        var bob = w.Current.Principal!;
        var admin = w.Users.Items.Single(u => u.IsAdmin);

        async Task<IReadOnlyList<AccessReadKey>> CompareAsync()
        {
            var reads = new KeepingReads();
            Assert.Equal(await AnswersAsync(w.Access, golf, fiat, alice, carol), await AnswersAsync(Keeping(w, reads), golf, fiat, alice, carol));
            Assert.Equal(reads.Read.Count, reads.Read.Distinct().Count()); // each at most once
            return reads.Read;
        }

        w.Current.SignInAs(w.Users.Items.Single(u => u.Id == alice));
        await CompareAsync();
        w.Current.SignInAs(admin);
        Assert.Empty(await CompareAsync()); // everything is the administrator's: no table read, as before
        w.Current.SignInAs(admin);
        await w.AccessAdmin.SetDefaultLevelAsync(AccessLevel.Edit, default);
        w.Current.Principal = bob;
        Assert.Contains(new AccessReadKey(AccessRead.Settings, Guid.Empty), await CompareAsync());
    }

    [Fact]
    public async Task InsideARun_TheTablesAreReadEachTime_NeverThroughTheRequestsCopy()
    {
        var (w, golf, fiat, alice, carol) = await SharedAsync();
        var reads = new KeepingReads();
        var bob = await w.Access.RequirePrincipalAsync(default);

        using (RunContext.Enter(new RunContext(bob, [golf.Id, fiat.Id])))
            await AnswersAsync(Keeping(w, reads), golf, fiat, alice, carol);

        Assert.Empty(reads.Read);
    }

    [Fact]
    public async Task AWriteToTheAccessTables_ClearsTheCopy_SoTheSameRequestReadsTheNewData()
    {
        var (w, golf, fiat, alice, _) = await SharedAsync();
        var reads = new KeepingReads();
        var access = Keeping(w, reads);
        var admin = new AccessAdminService(access, w.Settings, w.Grants, w.Users, w.Notifier, w.Log.For<AccessAdminService>());
        var sharing = new ResourceSharingService(w.Vehicles, w.ResourceGrants, w.Users, access, w.Notifier, w.Log.For<ResourceSharingService>());
        var bob = w.Current.Principal!;

        // Bob's Edit on the Fiat's logs is kept; Carol, its owner, takes it back: what was kept goes, and the next question sees it gone.
        Assert.Equal(AccessLevel.Edit, (await access.LogLevelsAsync([fiat], default))[fiat.Id]);
        w.Current.SignInAs(w.Users.Items.Single(u => u.Email == "carol@x.co"));
        await sharing.SetLogAccessAsync(fiat.Id, bob.Id, AccessLevel.None, default);
        Assert.Equal(1, reads.Cleared);
        w.Current.Principal = bob;
        Assert.Equal(AccessLevel.None, (await access.LogLevelsAsync([fiat], default))[fiat.Id]);

        // The administrator's changes to the default and to a grant, read afterwards in the same request.
        var root = w.Users.Items.Single(u => u.IsAdmin);
        w.Current.SignInAs(root);
        await admin.SetDefaultLevelAsync(AccessLevel.Edit, default);
        await admin.SetGrantAsync(alice, bob.Id, AccessLevel.None, default);
        Assert.Equal(3, reads.Cleared);
        w.Current.Principal = bob;
        Assert.Equal(AccessLevel.Edit, await access.LevelAsync(alice, default)); // the new default, the grant gone
        Assert.Equal(3, reads.Read.Count(k => k.Read == AccessRead.Settings)); // once at first, and again after each clear that was followed by a question
        _ = golf;
    }

    [Fact]
    public async Task OutsideARequest_EveryAskReads()
    {
        var calls = 0;
        var reads = new UncachedAccessReads();
        for (var i = 0; i < 3; i++) await reads.GetAsync(new AccessReadKey(AccessRead.Settings, Guid.Empty), _ => Task.FromResult(++calls), default);
        reads.Clear();
        Assert.Equal(3, calls);
    }
}
