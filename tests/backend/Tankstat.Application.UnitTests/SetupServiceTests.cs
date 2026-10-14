using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Setup;
using Tankstat.Domain;
using Tankstat.Domain.Instance;
using Tankstat.Domain.Users;
using Tankstat.TestSupport;

namespace Tankstat.Application.UnitTests;

/// <summary>The setup store in memory: decided while undecided only, completed while pending only, as the database's conditional updates do.</summary>
internal sealed class InMemorySetupStore(InMemoryUsers users, InMemoryVehicles vehicles) : ISetupStore
{
    public SetupPhase Phase { get; set; } = SetupPhase.Undecided;
    public SetupCompletion? Via { get; private set; }

    public Task<SetupPhase> PhaseAsync(CancellationToken ct) => Task.FromResult(Phase);

    public Task<bool> HoldsDataAsync(CancellationToken ct) => Task.FromResult(users.Items.Count > 0 || vehicles.Items.Count > 0);

    public Task<SetupPhase> DecideAsync(bool holdsData, DateTimeOffset now, CancellationToken ct)
    {
        if (Phase == SetupPhase.Undecided)
        {
            Phase = holdsData ? SetupPhase.Completed : SetupPhase.Pending;
            if (holdsData) Via = SetupCompletion.Upgrade;
        }
        return Task.FromResult(Phase);
    }

    public Task<bool> CompleteAsync(SetupCompletion via, DateTimeOffset now, CancellationToken ct)
    {
        if (Phase != SetupPhase.Pending) return Task.FromResult(false);
        (Phase, Via) = (SetupPhase.Completed, via);
        return Task.FromResult(true);
    }

    public Task<bool> CompleteWithAdministratorAsync(User administrator, DateTimeOffset now, CancellationToken ct)
    {
        if (Phase != SetupPhase.Pending) return Task.FromResult(false);
        users.Items.Add(administrator);
        (Phase, Via) = (SetupPhase.Completed, SetupCompletion.Administrator);
        return Task.FromResult(true);
    }
}

internal sealed class NoDatabaseFacts : IDatabaseFacts
{
    public Task<long?> SizeAsync(CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<string?> LastMigrationAsync(CancellationToken ct) => Task.FromResult<string?>("20261014021748_AddSetup");
    public Task<IReadOnlyList<string>> KnownMigrationsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(["20261014021748_AddSetup"]);
}

public sealed class SetupServiceTests
{
    private sealed record Setup(World World, InMemorySetupStore Store, SetupStatus Status, OneTimeCode Code, SetupService Service);

    private static Setup Make(AuthMode mode = AuthMode.Standalone, Action<AuthOptions>? configure = null, bool enabled = true)
    {
        var w = new World(mode, configure: configure);
        var store = new InMemorySetupStore(w.Users, w.Vehicles);
        var status = new SetupStatus();
        var code = new OneTimeCode();
        var service = new SetupService(
            store, status, code, w.Auth, w.Users, new NoBackups(), new NoDatabaseFacts(), new BackupOptions().Create(), w.Options.Create(),
            new SetupOptions { Enabled = enabled }.Create(), w.Clock, w.Log.For<SetupService>());
        return new Setup(w, store, status, code, service);
    }

    private static Action<AuthOptions> Bootstrap(string password = "initial-password-1") =>
        o => { o.Standalone.AdminEmail = "root@x.co"; o.Standalone.AdminPassword = password; };

    private static IEnumerable<LogEntry> CodeLines(Setup s) => s.World.Log.From<SetupService>().Where(e => e.Values.ContainsKey("Code"));

    [Theory]
    [InlineData(AuthMode.Standalone)]
    [InlineData(AuthMode.None)]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.ProxyHeader)]
    public async Task ANewEmptyInstance_Waits_AndLogsTheCodeOnce(AuthMode mode)
    {
        var s = Make(mode);

        await s.Service.StartAsync(default);

        Assert.Equal(SetupPhase.Pending, s.Store.Phase);
        Assert.True(s.Status.Pending);
        Assert.True((await s.Service.InfoAsync(default)).Pending);
        var line = Assert.Single(CodeLines(s));
        Assert.Equal(LogLevel.Warning, line.Level); // a log kept to warnings has it too
        Assert.Equal(s.Code.Value, line.Values["Code"]);
        Assert.Empty(s.World.Users.Items);
    }

    [Fact]
    public async Task ADatabaseThatHoldsData_CountsAsSetUp_ByTheUpgrade_AndLogsNoCode()
    {
        var s = Make(AuthMode.None);
        s.World.AddUser("someone@x.co");

        await s.Service.StartAsync(default);

        Assert.Equal((SetupPhase.Completed, SetupCompletion.Upgrade), (s.Store.Phase, s.Store.Via));
        Assert.False(s.Status.Pending);
        Assert.Empty(CodeLines(s));
    }

    [Fact]
    public async Task TheBootstrapAdministrator_CompletesTheSetup_ByTheEnvironment_DecidedBeforeItsOwnRowExists()
    {
        var s = Make(configure: Bootstrap());

        await s.Service.StartAsync(default);

        // Decided as a new instance first (the administrator made afterwards is no "data"), then completed by the environment.
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Environment), (s.Store.Phase, s.Store.Via));
        Assert.True(Assert.Single(s.World.Users.Items).IsAdmin);
        Assert.False(s.Status.Pending);
        Assert.Empty(CodeLines(s));
    }

    [Fact]
    public async Task AWeakBootstrapPassword_StillStopsTheStart()
    {
        var s = Make(configure: Bootstrap("weak"));

        await Assert.ThrowsAsync<DomainException>(() => s.Service.StartAsync(default));
    }

    [Theory]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.ProxyHeader)]
    public async Task AdministratorsTheEnvironmentNames_CompleteTheSetup(AuthMode mode)
    {
        var s = Make(mode, o => o.AdminEmails = ["boss@x.co"]);

        await s.Service.StartAsync(default);

        Assert.Equal((SetupPhase.Completed, SetupCompletion.Environment), (s.Store.Phase, s.Store.Via));
    }

    [Fact]
    public async Task SetupTurnedOff_CompletesIt_ByTheEnvironment()
    {
        var s = Make(AuthMode.None, enabled: false);

        await s.Service.StartAsync(default);

        Assert.Equal((SetupPhase.Completed, SetupCompletion.Environment), (s.Store.Phase, s.Store.Via));
        Assert.False(s.Status.Pending);
    }

    [Fact]
    public async Task ASetUpStandaloneInstance_WithoutAnAdministratorOrCredentials_StillStopsTheStart()
    {
        var s = Make();
        s.Store.Phase = SetupPhase.Completed;

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.StartAsync(default));
        Assert.Contains("Auth:Standalone:AdminEmail", e.Message);
    }

    [Fact]
    public async Task SetupTurnedOff_InStandalone_WithoutCredentials_StopsTheStart_AndALaterStartWithTheSetupOn_StillWaits()
    {
        var s = Make(enabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.StartAsync(default));

        Assert.Equal(SetupPhase.Pending, s.Store.Phase); // nothing completed by a start that stopped
    }

    [Theory]
    [InlineData("root@x.co", "")]
    [InlineData("", "initial-password-1")]
    public async Task OnlyOneOfTheBootstrapCredentials_StopsTheStart_NamingTheOneThatIsSet(string email, string password)
    {
        var s = Make(configure: o => { o.Standalone.AdminEmail = email; o.Standalone.AdminPassword = password; });

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.StartAsync(default));

        Assert.Contains(email.Length > 0 ? "Only Auth:Standalone:AdminEmail" : "Only Auth:Standalone:AdminPassword", e.Message);
        Assert.Empty(s.World.Users.Items);
    }

    [Fact]
    public async Task TheFirstAdministrator_IsMadeWithTheCode_TypedInAnyCase_WithOrWithoutHyphens()
    {
        var s = Make();
        await s.Service.StartAsync(default);
        var typed = " " + s.Code.Value.Replace("-", "").ToLowerInvariant() + "\n";

        var admin = await s.Service.CreateAdministratorAsync(typed, "Root@X.co", "initial-password-1", default);

        Assert.True(admin.IsAdmin);
        Assert.Equal("root@x.co", admin.Email);
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Administrator), (s.Store.Phase, s.Store.Via));
        Assert.False(s.Status.Pending);
        await s.World.Auth.LoginAsync("root@x.co", "initial-password-1", default);
        // Once completed, the code opens nothing again.
        var again = await Assert.ThrowsAsync<DomainException>(() => s.Service.CreateAdministratorAsync(s.Code.Value, "other@x.co", "initial-password-1", default));
        Assert.Equal("setup.completed", again.Key);
        Assert.Single(s.World.Users.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD-EFGH-JKLM")]
    public async Task AWrongCode_IsRefused_AndNothingIsWrittenOrLogged(string? typed)
    {
        var s = Make();
        await s.Service.StartAsync(default);
        s.World.Log.Clear();

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.CreateAdministratorAsync(typed, "zzzz@x.co", "zzzz-password-1", default));

        Assert.Equal("setup.codeInvalid", refused.Key);
        Assert.Empty(s.World.Users.Items);
        Assert.True(s.Status.Pending);
        Assert.False(s.World.Log.Mentions("zzzz"));
    }

    [Fact]
    public async Task APasswordThePolicyRefuses_LeavesTheSetupWaiting()
    {
        var s = Make();
        await s.Service.StartAsync(default);

        var refused = await Assert.ThrowsAsync<DomainException>(() => s.Service.CreateAdministratorAsync(s.Code.Value, "root@x.co", "short", default));

        Assert.Equal("password.tooShort", refused.Key);
        Assert.Equal(SetupPhase.Pending, s.Store.Phase);
        Assert.Empty(s.World.Users.Items);
    }

    [Fact]
    public async Task ACompletionAnotherServerMadeFirst_IsRefused_AndThisProcessLearnsIt()
    {
        var s = Make();
        await s.Service.StartAsync(default);
        var elsewhere = User.CreateLocal("first@x.co", "Administrator", isAdmin: true);
        await s.Store.CompleteWithAdministratorAsync(elsewhere, default, default); // another server on the database made the administrator

        var refused = await Assert.ThrowsAsync<DomainException>(() => s.Service.CreateAdministratorAsync(s.Code.Value, "root@x.co", "initial-password-1", default));

        Assert.Equal("setup.completed", refused.Key);
        Assert.False(s.Status.Pending); // the notice goes too
        Assert.Same(elsewhere, Assert.Single(s.World.Users.Items));
    }

    [Fact]
    public async Task TheSetupQuery_AsksTheDatabase_WhileThisProcessThinksItWaits()
    {
        var s = Make(AuthMode.None);
        await s.Service.StartAsync(default);
        await s.Store.CompleteAsync(SetupCompletion.Acknowledged, default, default); // acknowledged on another server

        Assert.False((await s.Service.InfoAsync(default)).Pending);
        Assert.False(s.Status.Pending);
    }

    [Fact]
    public async Task WithSignInOff_TheAcknowledgement_CompletesTheSetup()
    {
        var s = Make(AuthMode.None);
        await s.Service.StartAsync(default);

        var info = await s.Service.AcknowledgeOpenAccessAsync(s.Code.Value, default);

        Assert.False(info.Pending);
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Acknowledged), (s.Store.Phase, s.Store.Via));
    }

    [Fact]
    public async Task EachStep_BelongsToItsMode()
    {
        var standalone = Make();
        await standalone.Service.StartAsync(default);
        var none = Make(AuthMode.None);
        await none.Service.StartAsync(default);

        Assert.Equal("setup.otherMode", (await Assert.ThrowsAsync<DomainException>(() => standalone.Service.AcknowledgeOpenAccessAsync(standalone.Code.Value, default))).Key);
        Assert.Equal("setup.otherMode", (await Assert.ThrowsAsync<DomainException>(() => none.Service.CreateAdministratorAsync(none.Code.Value, "root@x.co", "initial-password-1", default))).Key);
        Assert.True(standalone.Status.Pending);
        Assert.True(none.Status.Pending);
    }

    [Fact]
    public async Task TheRestorePoints_NeedTheCode_WhileTheSetupWaits_AndNothingOnceItIsCompleted()
    {
        var s = Make();
        await s.Service.StartAsync(default);

        Assert.Equal("setup.codeInvalid", (await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.RestorePointsAsync("nope", default))).Key);
        var (points, _) = await s.Service.RestorePointsAsync(s.Code.Value, default);
        Assert.Empty(points.Points);
        Assert.Equal("20261014021748_AddSetup", points.DatabaseMigration);

        await s.Service.CreateAdministratorAsync(s.Code.Value, "root@x.co", "initial-password-1", default);
        Assert.Equal("setup.completed", (await Assert.ThrowsAsync<DomainException>(() => s.Service.RestorePointsAsync(s.Code.Value, default))).Key);
    }
}

public sealed class OneTimeCodeTests
{
    [Fact]
    public void TheCode_IsThreeGroupsOfFour_FromAnAlphabetWithoutLookalikes_AndNewForEveryStart()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => new OneTimeCode().Value).ToList();

        Assert.All(codes, c => Assert.Matches(new Regex("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$"), c));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void TheTypedCode_MatchesWhateverItsCase_Hyphens_OrSpaces_AndNothingElseMatches()
    {
        var code = new OneTimeCode();
        var plain = code.Value.Replace("-", "");

        Assert.True(code.Matches(code.Value));
        Assert.True(code.Matches(plain.ToLowerInvariant()));
        Assert.True(code.Matches($" {plain[..6]} {plain[6..]} "));
        Assert.False(code.Matches(null));
        Assert.False(code.Matches(""));
        Assert.False(code.Matches(plain[..11]));
        Assert.False(code.Matches(plain + "A"));
        Assert.False(code.Matches(new OneTimeCode().Value));
    }
}
