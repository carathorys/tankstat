using Tankstat.Application.Backups;
using Tankstat.Domain;

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
        public bool RestorePending => Marker is not null;
        public BackupManifest ReadManifest(string fileName) => Plain;
        public string? IdentityFor(BackupManifest manifest, string? passphrase, string? identity) => throw new NotSupportedException();
        public Task RequireSameMigrationAsync(BackupManifest manifest, CancellationToken ct) =>
            OtherMigration is null ? Task.CompletedTask : throw new DomainException("backup.otherMigration", "Another migration.", new { Backup = manifest.Migration, Database = OtherMigration });
        public void RequestRestore(RestoreMarker marker) => Marker = marker;
        public IReadOnlyList<RestorePoint> List() => [];
        public string Folder => "backups";
        public Task<RestorePoint> SaveUploadAsync(Stream content, string? fileName, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Stream OpenRead(string fileName) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
    }

    private static (World World, OneBackup Store, RestoreService Service) Admin(OneBackup? store = null)
    {
        var w = new World();
        w.Current.SignInAs(w.AddUser("root@x.co", admin: true));
        store ??= new OneBackup();
        return (w, store, new RestoreService(store, w.Access, w.Options.Create(), new RestoreCode(), w.SystemConfiguration, w.Restart, w.Clock, w.Log.For<RestoreService>()));
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
    public async Task ABackupOfAnotherMigration_IsRefusedAtOnce_NotAfterARestartThatComesBackWithTheDataAsItWas()
    {
        var (w, store, service) = Admin(new OneBackup { OtherMigration = "20261101000000_SomethingNewer" });

        var refused = await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(new RestoreRequest("tankstat-backup-1.zip", null, null, null), default));

        Assert.Equal("backup.otherMigration", refused.Key);
        Assert.Null(store.Marker);
        Assert.Equal(0, w.Restart.Count);
    }
}
