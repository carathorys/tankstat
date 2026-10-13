using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tankstat.Application;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// Upgrades, from every migration of the release: a database at it is backed up at start before anything is migrated (the backup holds it as
/// that migration left it), then brought up to date; and that backup, an older migration's, is restored and brought up to date again. A
/// backup that cannot be written stops the migration.
/// </summary>
public sealed class MigrationUpgradeTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-upgrade-{Guid.NewGuid():N}");
    private readonly CapturedLog _log = new();
    private ServiceProvider? _services;

    private string Database => Path.Combine(_root, "tankstat.db");
    private string Backups => Path.Combine(_root, "backups");

    /// <summary>Every migration of the release but the last, by name (a database at the last one has nothing to upgrade).</summary>
    public static TheoryData<string> OlderMigrations() => [.. OlderMigrationNames()];

    private static List<string> OlderMigrationNames()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:", o => o.MigrationsAssembly("Tankstat.Migrations.Sqlite")).Options);
        var ids = db.Database.GetMigrations().ToList();
        return [.. ids.Take(ids.Count - 1).Select(id => MigrationNames.Of(id)!)];
    }

    private ServiceProvider Services(string? backups = null)
    {
        Directory.CreateDirectory(_root);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={Database}",
            ["Storage:Path"] = Path.Combine(_root, "uploads"),
            ["DataProtection:KeysPath"] = Path.Combine(_root, "keys"),
            ["Backups:Path"] = backups ?? Backups,
        }).Build();
        return _services = new ServiceCollection().AddLogging(b => b.AddProvider(_log)).AddApplication(config).AddInfrastructure(config).BuildServiceProvider();
    }

    private async Task<AppDbContext> Db() => await _services!.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    private T Step<T>() => _services!.GetServices<IHostedService>().OfType<T>().Single();

    /// <summary>The database set up as the migration named <paramref name="name"/> leaves it; its provider's id.</summary>
    private async Task<string> AtMigration(string name)
    {
        await using var db = await Db();
        var id = MigrationModels.IdOf(db, name)!;
        await db.GetService<IMigrator>().MigrateAsync(id);
        return id;
    }

    private async Task<string?> LastApplied()
    {
        await using var db = await Db();
        return (await db.Database.GetAppliedMigrationsAsync()).LastOrDefault();
    }

    [Theory]
    [MemberData(nameof(OlderMigrations))]
    public async Task ADatabaseAtAnOlderMigration_IsBackedUpBeforeItIsMigrated_AndThatBackupRestoresAndComesUpToDate(string name)
    {
        Services();
        var id = await AtMigration(name);
        string latest;
        await using (var db = await Db()) latest = db.Database.GetMigrations().Last();

        await Step<DatabaseMigrator>().StartAsync(default);

        Assert.Equal(latest, await LastApplied());
        var backup = Assert.Single(Directory.GetFiles(Backups, "*.zip"));
        var manifest = BackupFile.ReadManifest(backup);
        Assert.Equal((id, true), (manifest.Migration, manifest.SafetyNet)); // written before anything was migrated
        var lines = _log.From<DatabaseMigrator>().Select(e => e.Values.ContainsKey("Migrations") ? "applying" : e.Message.StartsWith("Backing up", StringComparison.Ordinal) ? "backup" : "other").ToList();
        Assert.True(lines.IndexOf("backup") is >= 0 and var b && b < lines.IndexOf("applying"), string.Join(", ", lines));

        // That backup is an older migration's: restored, the database is set up at it, loaded, and brought up to date.
        new RestoreMarker(Path.GetFileName(backup), Guid.Empty, DateTimeOffset.UtcNow, null).Write(Backups);
        var restore = Step<RestoreStep>();
        await restore.StartAsync(default);

        Assert.Equal(RestoreOutcome.Applied, restore.Outcome);
        Assert.Equal(latest, await LastApplied());
        Assert.False(File.Exists(RestoreMarker.PathIn(Backups)));
    }

    [Fact]
    public async Task ABackupThatCannotBeWritten_StopsTheMigration_AndTheDatabaseStaysAsItWas()
    {
        Directory.CreateDirectory(_root);
        var notAFolder = Path.Combine(_root, "backups-is-a-file");
        await File.WriteAllTextAsync(notAFolder, "");
        Services(backups: notAFolder);
        var id = await AtMigration(OlderMigrationNames()[^1]);

        var stopped = await Assert.ThrowsAsync<InvalidOperationException>(() => Step<DatabaseMigrator>().StartAsync(default));

        Assert.Contains("was not migrated", stopped.Message);
        Assert.Equal(id, await LastApplied());
    }

    [Fact]
    public async Task AStartWithARestoreWaiting_WritesNoBackupBeforeTheMigrations_TheRestoreWritesItsOwn()
    {
        Services();
        await AtMigration(OlderMigrationNames()[^1]);
        Directory.CreateDirectory(Backups);
        new RestoreMarker("tankstat-backup-20260101T000000Z.zip", Guid.Empty, DateTimeOffset.UtcNow, null).Write(Backups);

        await Step<DatabaseMigrator>().StartAsync(default);

        Assert.Empty(Directory.GetFiles(Backups, "*.zip"));
        await using var db = await Db();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task ANewDatabase_IsMigrated_WithNoBackup_ForItHoldsNothing()
    {
        Services();

        await Step<DatabaseMigrator>().StartAsync(default);

        Assert.False(Directory.Exists(Backups) && Directory.GetFiles(Backups).Length > 0);
        Assert.NotNull(await LastApplied());
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null) await _services.DisposeAsync();
        using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Database}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
