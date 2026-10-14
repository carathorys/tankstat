using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Setup;
using Tankstat.Application.SystemSettings;
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

    public Task<bool> CompleteWithClaimAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        if (Phase != SetupPhase.Pending) return Task.FromResult(false);
        users.Items.Single(u => u.Id == userId).SetAdmin(true);
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
        w.SystemConfiguration.Shipped["Auth:Mode"] = mode.ToString(); // what the instance runs, as appsettings.json says it
        w.SetupStore.Phase = SetupPhase.Undecided; // a new database: its start decides
        var service = new SetupService(
            w.SetupStore, w.SetupStatus, w.SetupGate, w.Code, w.Auth, w.Access, w.SystemSettings, w.SystemConfiguration, w.Restart, w.Users, new NoBackups(),
            new NoSettingsFile(), new NoDatabaseFacts(), new BackupOptions().Create(), w.Options.Create(), new SetupOptions { Enabled = enabled }.Create(), w.Clock, w.Log.For<SetupService>());
        return new Setup(w, w.SetupStore, w.SetupStatus, w.Code, service);
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
    public async Task LosingTheBootstrapAdministrator_ToAnotherServerStartingAtOnce_CompletesTheSetup_NotLeavesItWaiting()
    {
        // Both servers find no administrator and both insert; the unique index keeps the other's. The setup is then complete, not pending.
        var s = Make(configure: Bootstrap());
        s.World.Users.BeforeAdd = _ => s.World.AddUser("root@x.co", admin: true);

        await s.Service.StartAsync(default);

        Assert.Equal((SetupPhase.Completed, SetupCompletion.Environment), (s.Store.Phase, s.Store.Via));
        Assert.False(s.Status.Pending);
        Assert.Empty(CodeLines(s));
        Assert.Single(s.World.Users.Items);
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

        var admin = (await s.Service.FinishAsync(typed, "Root@X.co", "initial-password-1", false, default)).Administrator!;

        Assert.True(admin.IsAdmin);
        Assert.Equal("root@x.co", admin.Email);
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Administrator), (s.Store.Phase, s.Store.Via));
        Assert.False(s.Status.Pending);
        await s.World.Auth.LoginAsync("root@x.co", "initial-password-1", default);
        // Once completed, the code opens nothing again.
        var again = await Assert.ThrowsAsync<DomainException>(() => s.Service.FinishAsync(s.Code.Value, "other@x.co", "initial-password-1", false, default));
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

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.FinishAsync(typed, "zzzz@x.co", "zzzz-password-1", false, default));

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

        var refused = await Assert.ThrowsAsync<DomainException>(() => s.Service.FinishAsync(s.Code.Value, "root@x.co", "short", false, default));

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

        var refused = await Assert.ThrowsAsync<DomainException>(() => s.Service.FinishAsync(s.Code.Value, "root@x.co", "initial-password-1", false, default));

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

        Assert.Equal("setup.acknowledgementRequired", (await Assert.ThrowsAsync<DomainException>(() => s.Service.FinishAsync(s.Code.Value, null, null, false, default))).Key);
        var finished = await s.Service.FinishAsync(s.Code.Value, null, null, true, default);

        Assert.Equal((false, (User?)null), (finished.Restarting, finished.Administrator)); // nothing the wizard saved waits for a restart
        Assert.False(s.Status.Pending);
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Acknowledged), (s.Store.Phase, s.Store.Via));
    }

    [Theory]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.ProxyHeader)]
    public async Task WithAProvider_TheSignedInUser_ClaimsTheAdministrator_WithTheCode(AuthMode mode)
    {
        var s = Make(mode);
        await s.Service.StartAsync(default);
        var someone = s.World.AddUser("someone@x.co");

        await Assert.ThrowsAsync<UnauthenticatedException>(() => s.Service.ClaimAdministratorAsync(s.Code.Value, default)); // nobody signed in
        s.World.Current.SignInAs(someone);
        Assert.Equal("setup.codeInvalid", (await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.ClaimAdministratorAsync("nope", default))).Key);
        Assert.False(someone.IsAdmin);

        var claimed = await s.Service.ClaimAdministratorAsync(s.Code.Value, default);

        Assert.True(claimed.IsAdmin);
        Assert.True(someone.IsAdmin);
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Administrator), (s.Store.Phase, s.Store.Via));
        Assert.Contains(s.World.Log.From<SetupService>(), e => e.Values.TryGetValue("UserId", out var id) && (Guid?)id == someone.Id);
    }

    [Fact]
    public async Task TheWizardsSettings_AreSavedWithTheCode_AndTheLastStep_IsTheNextModesGatekeeper_ThenOneRestart()
    {
        var s = Make(AuthMode.None);
        await s.Service.StartAsync(default);
        Assert.Equal("setup.codeInvalid", (await Assert.ThrowsAsync<ForbiddenException>(() => s.World.SystemSettings.GetAsync("nope", default))).Key);

        // Standalone chosen in the wizard: no administrator and no credentials, which the settings page refuses once the setup is done, since
        // the start would stop; while it waits the start waits for the setup page instead.
        await s.World.SystemSettings.UpdateAsync([new SettingChange("Auth:Mode", "Standalone")], s.Code.Value, default);
        Assert.Contains(s.World.Log.From<SystemSettingsService>(), e => e.Values.TryGetValue("Keys", out var keys) && (string?)keys == "Auth:Mode");
        Assert.Equal(0, s.World.Restart.Count); // the one restart comes with the last step

        var finished = await s.Service.FinishAsync(s.Code.Value, "root@x.co", "initial-password-1", false, default);

        Assert.True(finished.Administrator!.IsAdmin); // the mode the next start runs, although sign-in is off until the restart
        Assert.True(finished.Restarting);
        Assert.Equal(1, s.World.Restart.Count);
        Assert.Equal((SetupPhase.Completed, SetupCompletion.Administrator), (s.Store.Phase, s.Store.Via));
        Assert.Equal("setup.completed", (await Assert.ThrowsAsync<DomainException>(() => s.World.SystemSettings.GetAsync(s.Code.Value, default))).Key);
    }

    [Theory]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.ProxyHeader)]
    public async Task WithAProvider_TheLastStep_CompletesNothing_TheClaimFollows(AuthMode mode)
    {
        var s = Make(mode);
        await s.Service.StartAsync(default);

        var finished = await s.Service.FinishAsync(s.Code.Value, null, null, false, default);

        Assert.Equal((false, (User?)null), (finished.Restarting, finished.Administrator));
        Assert.True(s.Status.Pending); // the administrators the settings name complete it at the next start, else the claim
    }

    [Fact]
    public async Task TheClaim_BelongsToAProvider()
    {
        var s = Make();
        await s.Service.StartAsync(default);

        Assert.Equal("setup.otherMode", (await Assert.ThrowsAsync<DomainException>(() => s.Service.ClaimAdministratorAsync(s.Code.Value, default))).Key);
        Assert.True(s.Status.Pending);
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

        await s.Service.FinishAsync(s.Code.Value, "root@x.co", "initial-password-1", false, default);
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
