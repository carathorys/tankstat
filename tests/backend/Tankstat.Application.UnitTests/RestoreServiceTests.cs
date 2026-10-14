using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Setup;
using Tankstat.Application.SystemSettings;
using Tankstat.Domain;
using Tankstat.Domain.Instance;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// Asking for a restore restarts the server in its process, as an administrator's restart does, and never when the start would refuse the
/// backup or the next start the settings file.
/// </summary>
public sealed class RestoreServiceTests
{
    private static readonly BackupManifest Plain = new(
        BackupManifest.CurrentFormat, BackupManifest.Tankstat, "1.0.0", new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero), "Sqlite", "20261012093636_AddInstanceGeneration",
        false, new BackupContents(new Dictionary<string, long>(), true, BackupPhotos.WithPictures, false), new BackupSizes(0, 0, 0, 0, 0), new BackupArchive(BackupArchive.Plain, ""), null);

    private sealed class OneBackup : IBackupStore
    {
        public RestoreMarker? Marker { get; private set; }
        /// <summary>The database's migration, when it is not the backup's.</summary>
        public string? OtherMigration { get; init; }
        /// <summary>The settings file the backup carries; none (an older backup) when null.</summary>
        public IReadOnlyDictionary<string, string?>? Settings { get; init; }
        public bool RestorePending => Marker is not null;
        /// <summary>What the backup recorded of its server's environment; nothing (an older backup) when null.</summary>
        public BackupEnvironment? Recorded { get; init; }
        public BackupManifest ReadManifest(string fileName) => Plain with { Environment = Recorded };
        public IReadOnlyDictionary<string, string?>? ReadSettings(string fileName, BackupManifest manifest, string? identity) => Settings;
        public string? IdentityFor(BackupManifest manifest, string? passphrase, string? identity) => throw new NotSupportedException();
        public Task RequireRestorableMigrationAsync(BackupManifest manifest, CancellationToken ct) =>
            OtherMigration is null ? Task.CompletedTask : throw new DomainException("backup.otherMigration", "Another migration.", new { Backup = manifest.Migration, Database = OtherMigration });
        public void RequestRestore(RestoreMarker marker) => Marker = marker;
        public IReadOnlyList<RestorePoint> List() => [];
        public string Folder => "backups";
        public Task<RestorePoint> SaveUploadAsync(Stream content, string? fileName, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Stream OpenRead(string fileName) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
        public RestorePoint Find(string fileName) => throw new NotSupportedException();
        public void Hold(string fileName, DateTimeOffset? since) => throw new NotSupportedException();
    }

    /// <summary>The settings file as this server would judge a restored one: the problems it is given, the values it was asked about.</summary>
    private sealed class JudgedSettings : ISettingsBackup
    {
        public IReadOnlyList<string> Problems { get; init; } = [];
        /// <summary>The recorded settings this server lacks, as it would judge them.</summary>
        public IReadOnlyList<string> Missing { get; init; } = [];
        public IReadOnlyDictionary<string, string?>? Asked { get; private set; }
        public Task<SettingsCopy> CopyAsync(bool carriesSecrets, BackupEnvironmentMode mode, CancellationToken ct) => Task.FromResult(SettingsCopy.None);
        public Task<IReadOnlyList<string>> MissingAsync(BackupEnvironment recorded, IReadOnlyDictionary<string, string?>? restored, CancellationToken ct) => Task.FromResult(Missing);
        public IReadOnlyList<string> Overridden(IReadOnlyDictionary<string, string?> fromBackup) => [];
        public Task<RestoredSettings> RestoredAsync(IReadOnlyDictionary<string, string?> fromBackup, bool carriesSecrets, CancellationToken ct)
        {
            Asked = fromBackup;
            return Task.FromResult(new RestoredSettings(fromBackup, Problems, new AuthOptions()));
        }
        public Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct) => throw new NotSupportedException(); // the start writes it, never the request
    }

    private static (World World, OneBackup Store, RestoreService Service) Admin(OneBackup? store = null, JudgedSettings? settings = null)
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));
        store ??= new OneBackup();
        return (w, store, Service(w, store, new OneTimeCode(), new SetupStatus(), settings: settings));
    }

    private static RestoreService Service(World w, OneBackup store, OneTimeCode code, SetupStatus setup, SetupPhase phase = SetupPhase.Completed, JudgedSettings? settings = null) =>
        new(store, w.Access, w.Options.Create(), code, new SetupGate(setup, new InMemorySetupStore(w.Users, w.Vehicles) { Phase = phase }, code), w.SystemConfiguration,
            settings ?? new JudgedSettings(), w.Restart, w.Clock, w.Log.For<RestoreService>());

    /// <summary>A new Standalone instance whose setup waits: nobody signed in, no administrator anywhere.</summary>
    private static (World World, OneBackup Store, OneTimeCode Code, RestoreService Service) WaitingForSetup()
    {
        var w = new World();
        var store = new OneBackup();
        var code = new OneTimeCode();
        var setup = new SetupStatus();
        setup.Set(pending: true);
        return (w, store, code, Service(w, store, code, setup, SetupPhase.Pending));
    }

    [Fact]
    public async Task WhileTheSetupWaits_TheCodeFromTheLog_AsksForTheRestore_WithoutAnAdministrator()
    {
        var (w, store, code, service) = WaitingForSetup();

        await service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, code.Value.ToLowerInvariant()), default);

        Assert.Equal(Principal.Anonymous.Id, store.Marker!.RequestedById);
        Assert.Equal(1, w.Restart.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ABCD-EFGH-JKLM")]
    public async Task WhileTheSetupWaits_AWrongOrMissingCode_IsRefused_AndNothingIsWritten(string? typed)
    {
        var (w, store, _, service) = WaitingForSetup();

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, typed), default));

        Assert.Equal("backup.codeRequired", refused.Key);
        Assert.Null(store.Marker);
        Assert.Equal(0, w.Restart.Count);
    }

    [Fact]
    public async Task OnceTheSetupIsCompleted_TheCodeOpensNothing_AnAdministratorIsNeeded()
    {
        var w = new World();
        var store = new OneBackup();
        var code = new OneTimeCode();
        var service = Service(w, store, code, new SetupStatus());

        await Assert.ThrowsAsync<UnauthenticatedException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, code.Value), default));

        Assert.Null(store.Marker);
    }

    [Fact]
    public async Task ASetupAnotherServerCompleted_IsClosedToTheCode_AndItsAdministratorRestores()
    {
        // This process still thinks the setup waits; the database says another server on it completed the setup since.
        var w = new World();
        var store = new OneBackup();
        var code = new OneTimeCode();
        var setup = new SetupStatus();
        setup.Set(pending: true);
        var service = Service(w, store, code, setup, SetupPhase.Completed);

        await Assert.ThrowsAsync<UnauthenticatedException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, code.Value), default));
        Assert.False(setup.Pending);
        Assert.Null(store.Marker);

        var admin = w.AddUser("root@x.co", admin: true);
        w.Current.SignInAs(admin);
        await service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default);
        Assert.Equal(admin.Id, store.Marker!.RequestedById);
    }

    [Fact]
    public async Task ARestore_WritesTheMarker_ThenRestartsTheServer()
    {
        var (w, store, service) = Admin();

        var pending = await service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default);

        Assert.Equal(("tankstat-backup-1.zip", Plain.CreatedAt), (pending.File, pending.RestorePoint));
        Assert.Equal("tankstat-backup-1.zip", store.Marker!.File);
        Assert.Equal(1, w.Restart.Count);
    }

    [Fact]
    public async Task ARestoreTheNextStartWouldRefuse_WritesNoMarker_AndRestartsNothing()
    {
        var (w, store, service) = Admin();
        w.SystemConfiguration.NextStartRefusal = ["Notifications:MaxPerHour must be at least 1."]; // a file hand-edited since the start

        var refused = await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default));

        Assert.Equal("settings.restartWouldFail", refused.Key);
        Assert.Null(store.Marker);
        Assert.Equal(0, w.Restart.Count);
    }

    [Fact]
    public async Task ABackupWithASettingsFile_IsJudgedByThatFile_NotByTheOneItReplaces()
    {
        var carried = new Dictionary<string, string?> { ["Auth:Mode"] = "Standalone" };
        var settings = new JudgedSettings();
        var (w, store, service) = Admin(new OneBackup { Settings = carried }, settings);
        w.SystemConfiguration.NextStartRefusal = ["Notifications:MaxPerHour must be at least 1."]; // this server's own file: the restore replaces it

        await service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default);

        Assert.Same(carried, settings.Asked);
        Assert.NotNull(store.Marker);
        Assert.Equal(1, w.Restart.Count);
    }

    [Fact]
    public async Task ABackupWhoseSettingsFileThisServerWouldNotStartWith_IsRefused_NamingTheSettings_AndNothingIsWritten()
    {
        var (w, store, service) = Admin(new OneBackup { Settings = new Dictionary<string, string?> { ["Auth:Mode"] = "Oidc" } },
            new JudgedSettings { Problems = ["Auth:Oidc:Authority is required for Oidc mode."] });

        var refused = await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default));

        Assert.Equal("backup.settingsRefused", refused.Key);
        Assert.Contains("Auth:Oidc:Authority", refused.Message);
        Assert.Null(store.Marker);
        Assert.Equal(0, w.Restart.Count);
    }

    [Fact]
    public async Task ABackupWhoseServerSetSettingsInItsEnvironment_ThatThisServerLacks_IsRefused_NamingThem_AndNothingIsWritten()
    {
        var (w, store, service) = Admin(new OneBackup { Recorded = new BackupEnvironment(BackupEnvironmentMode.Record, ["Auth__Mode", "Smtp__Host"], []) },
            new JudgedSettings { Missing = ["Smtp__Host"] });

        var refused = await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default));

        Assert.Equal("backup.environmentMissing", refused.Key);
        Assert.Contains("Smtp__Host", refused.Message);
        Assert.Null(store.Marker);
        Assert.Equal(0, w.Restart.Count);
    }

    [Fact]
    public async Task ABackupOfAnotherMigration_IsRefusedAtOnce_NotAfterARestartThatComesBackWithTheDataAsItWas()
    {
        var (w, store, service) = Admin(new OneBackup { OtherMigration = "20261101000000_SomethingNewer" });

        var refused = await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default));

        Assert.Equal("backup.otherMigration", refused.Key);
        Assert.Null(store.Marker);
        Assert.Equal(0, w.Restart.Count);
    }
}
