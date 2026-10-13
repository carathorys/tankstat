using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Images;
using Tankstat.Application.Users;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Domain.Images;
using Tankstat.Domain.Instance;
using Tankstat.Domain.Users;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>What the start did with a restore that waited for it (<see cref="RestoreStep.Outcome"/>).</summary>
public enum RestoreOutcome
{
    /// <summary>No restore waited.</summary>
    None,
    /// <summary>The data is the backup's.</summary>
    Applied,
    /// <summary>Refused before anything was replaced: the data is as it was (<c>restore.refused.&lt;time&gt;</c>).</summary>
    Refused,
    /// <summary>The database was replaced, then the files failed: they may be half copied (<c>restore.failed.&lt;time&gt;</c>).</summary>
    Failed,
}

/// <summary>
/// At start, after the database migrations and before anything else touches the data: applies a restore that waits in the backups
/// folder (<see cref="RestoreMarker"/>), and gives a fresh instance its generation; <see cref="InstanceGeneration"/> then knows which generation
/// the data has, for every session answer.
/// <para>
/// A restore replaces the instance's data as a whole, never merges: the manifest and the checksum are checked, the backup's migration must
/// be the one the database is at (this release's), an encrypted backup's identity must open it; then a backup of the state about to be
/// replaced is written first (the safety net, holding whatever the restore replaces; without it nothing goes on), the file is unpacked into
/// a temporary folder, every table is loaded in one transaction (<see cref="TableLoad"/>, which stamps the new generation last; a Standalone
/// instance refuses rows without an administrator to sign in with), the contents of the pictures root (and of the photos root, when the
/// backup holds it) and of the keys folder (an encrypted backup) are replaced, and the marker goes. A refused restore renames the marker to
/// <c>restore.refused.&lt;time&gt;</c> and the start goes on with the data as it is; a failure after the database was replaced (the files
/// half copied) renames it to <c>restore.failed.&lt;time&gt;</c>, and the safety net is the way back. A renamed marker never keeps the
/// identity: it opens every backup of the folder. Nothing here ever stops the app.
/// </para>
/// </summary>
internal sealed class RestoreStep(
    IDbContextFactory<AppDbContext> dbFactory,
    DatabaseMigrator migrator,
    IBackupWriter writer,
    IOptionsMonitor<BackupOptions> options,
    IOptions<StorageOptions> storage,
    IOptions<KeyRingOptions> keyRing,
    IOptions<AuthOptions> auth,
    InstanceGeneration state,
    RestoreCode code,
    TimeProvider clock,
    ILogger<RestoreStep> logger) : IHostedService
{
    /// <summary>The folders right below a vehicle's that hold the photos of its logs (not their drafts, which go with every restore).</summary>
    private static readonly HashSet<string> LogPhotoFolders = [.. ImageFolders.LogPhotoKinds];

    /// <summary>What this start did with a restore that waited for it.</summary>
    public RestoreOutcome Outcome { get; private set; }

    public async Task StartAsync(CancellationToken ct)
    {
        var marker = RestoreMarker.PathIn(options.CurrentValue.Root);
        Outcome = File.Exists(marker) ? await RestoreAsync(marker, ct) : RestoreOutcome.None;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // A fresh instance, or one whose row was written before generations existed, gets its first generation; InstanceStartService
        // records the start in this row afterwards. Both safe when two instances start on one database at once: the row another one
        // wrote first, and the generation it stamped first, are the ones kept (and read back).
        if (!await db.InstanceState.AnyAsync(ct))
        {
            db.InstanceState.Add(InstanceState.New());
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }
        }
        else
        {
            var first = Guid.NewGuid(); // a value, sent as a parameter: no provider has to translate making one
            if (await db.InstanceState.Where(s => s.Generation == Guid.Empty).ExecuteUpdateAsync(s => s.SetProperty(x => x.Generation, first), ct) > 0)
                logger.LogDebug("The instance got its first generation");
        }
        var instance = await db.InstanceState.AsNoTracking().SingleAsync(ct);
        state.Set(new InstanceInfo(instance.Generation, instance.RestorePoint));

        if (auth.Value.Mode == AuthMode.None)
            logger.LogInformation("Restoring a backup from the app needs the one-time restore code {Code} (sign-in is off, so there is no administrator to ask)", code.Value);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task<RestoreOutcome> RestoreAsync(string markerPath, CancellationToken ct)
    {
        RestoreMarker marker;
        try
        {
            marker = RestoreMarker.Read(markerPath);
            if (string.IsNullOrWhiteSpace(marker.File)) throw new JsonException("The marker names no backup.");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Refuse(markerPath, null, e, "the marker cannot be read");
        }
        var file = Path.Combine(options.CurrentValue.Root, Path.GetFileName(marker.File));
        BackupManifest manifest;
        byte[]? identity = null;
        string? replayFrom;
        try
        {
            if (!File.Exists(file)) throw new FileNotFoundException("The backup is not in the backups folder.", marker.File);
            manifest = BackupFile.ReadManifest(file);
            if (manifest.Encryption is { } encryption)
            {
                identity = marker.Identity is null ? null : AgeKeys.ParseIdentity(marker.Identity);
                if (identity is null || !BackupKeys.Opens(identity, encryption.Recipient))
                    throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
            }
            replayFrom = await FileBackupStore.ReplayFromAsync(dbFactory, manifest, ct);
            BackupFile.VerifyChecksum(file, manifest);
        }
        catch (Exception e) when (e is DomainException or IOException or UnauthorizedAccessException or FormatException)
        {
            return Refuse(markerPath, marker, e, e is DomainException d ? d.Key : "the backup cannot be read");
        }

        logger.LogInformation("Restoring the backup {File} (written by Tankstat {Version} on {CreatedAt}, {Provider}), asked for by user {UserId}",
            Path.GetFileName(file), manifest.AppVersion, manifest.CreatedAt, manifest.Provider, marker.RequestedById);
        try
        {
            // Of everything the restore replaces: a photos root of its own too when the backup brings one in its place, whatever Backups:Photos
            // leaves out of the other backups (on one root the photos are with the pictures anyway).
            await writer.WriteAsync(new BackupRequest(Photos: manifest.Contents.Photos == BackupPhotos.Separate ? true : null, SafetyNet: true), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Refuse(markerPath, marker, e, "the safety-net backup of the data about to be replaced could not be written");
        }

        DirectoryInfo? scratch = null;
        var loaded = false;
        try
        {
            scratch = Directory.CreateTempSubdirectory("tankstat-restore-");
            BackupFile.Extract(file, manifest, identity, scratch.FullName);
            var tables = Path.Combine(scratch.FullName, "tables");
            InstanceInfo instance;
            if (replayFrom is null)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                instance = await TableLoad.LoadAsync(db, tables, manifest.Contents.Tables, clock.GetUtcNow(), manifest.CreatedAt, logger, ct, RequireAdministratorAsync);
            }
            else instance = await ReplayAsync(replayFrom, manifest, tables, () => loaded = true, ct);
            loaded = true;
            var roots = storage.Value;
            // A backup without the photos: the photos of logs on disk keep matching their logs' ids and stay, also those still below the
            // pictures root (one root, or not moved to a photos root of their own yet); everything else of the root is the backup's.
            var keepPhotos = manifest.Contents.Photos == BackupPhotos.LeftOut;
            if (manifest.Contents.Pictures) ReplaceContents(roots.PicturesRoot, Path.Combine(scratch.FullName, "files", "pictures"), "pictures", keepPhotos);
            if (manifest.Contents.Photos == BackupPhotos.Separate)
            {
                // Their own root now too: replaced; one root now: they join the pictures just restored.
                if (roots.OneRoot) CopyContents(Path.Combine(scratch.FullName, "files", "photos"), roots.PhotosRoot);
                else ReplaceContents(roots.PhotosRoot, Path.Combine(scratch.FullName, "files", "photos"), "photos");
            }
            if (manifest.Contents.Keys) ReplaceKeys(keyRing.Value.Root, Path.Combine(scratch.FullName, "keys"));
            File.Delete(markerPath);
            logger.LogInformation("Restored the backup {File}: the data is generation {Generation} now, taken back to {RestorePoint}", Path.GetFileName(file), instance.Generation, manifest.CreatedAt);
            return RestoreOutcome.Applied;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (!loaded) return Refuse(markerPath, marker, e, e is DomainException d ? d.Key : "it could not be unpacked or loaded; the database is as it was");
            logger.LogError(e, "The restore of {File} failed after the database was replaced; the files may be half copied, the safety-net backup is the way back", Path.GetFileName(file));
            if (replayFrom is not null) await BringUpToDateAsync();
            Rename(markerPath, marker, "restore.failed");
            return RestoreOutcome.Failed;
        }
        finally
        {
            try
            {
                scratch?.Delete(recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // It holds the backup's tables (and keys) in plain: the operator removes it, or the system clears it.
                logger.LogWarning(e, "The temporary folder the backup was unpacked into could not be removed: {Folder}", scratch!.FullName);
            }
        }
    }

    /// <summary>
    /// A backup made at an older migration of this release: what can be checked is checked first (the administrator Standalone needs, from
    /// the backup's own rows), then the database is emptied (every table of this release, and the migrations history), set up as the
    /// backup's migration left it, loaded with that migration's model, and brought up to date by the migrator, with what follows the newer
    /// migrations (the stored consumption, ...); the generation is stamped last. EF runs each migration in a transaction of its own, so
    /// nothing after the first table dropped can be undone: <paramref name="destructive"/> marks that point, after which a failure is a
    /// failed restore (the schema brought up to date again, the safety-net backup the way back), never a refused one.
    /// </summary>
    private async Task<InstanceInfo> ReplayAsync(string from, BackupManifest manifest, string tables, Action destructive, CancellationToken ct)
    {
        IModel model;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            model = MigrationModels.ModelOf(db, from);
        await RequireAdministratorAsync(() => Task.FromResult(BackupHoldsAdministrator(model, tables)), subject => Task.FromResult(BackupHoldsLocalUser(model, tables, subject)));

        logger.LogInformation("The backup was made at an older migration ({Migration}): the database is emptied, set up as that migration left it, loaded, and brought up to date",
            MigrationNames.Of(from));
        destructive();
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await DropAllAsync(db, ct);
            await db.GetService<IMigrator>().MigrateAsync(from, ct);
            await TableLoad.LoadRowsAsync(db, model, tables, manifest.Contents.Tables, logger, ct);
        }
        await migrator.MigrateAsync(ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            return await TableLoad.StampAsync(db, clock.GetUtcNow(), manifest.CreatedAt, ct);
    }

    /// <summary>Every table of this release (its children first) and the migrations' own, so the migrations can set the database up afresh.</summary>
    private static async Task DropAllAsync(AppDbContext db, CancellationToken ct)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var names = TableLoad.InLoadOrder(TableDump.Tables(db.Model)).Reverse().Select(t => sql.DelimitIdentifier(t.Name, t.Schema))
            .Concat([sql.DelimitIdentifier(TableDump.MigrationsHistory), sql.DelimitIdentifier("__EFMigrationsLock")]);
        foreach (var name in names)
        {
            var drop = $"DROP TABLE IF EXISTS {name}"; // names from the model, never from the backup
            await db.Database.ExecuteSqlRawAsync(drop, ct);
        }
    }

    /// <summary>After a replay that failed half way: the schema up to date again, so the rest of the start meets the tables it knows.</summary>
    private async Task BringUpToDateAsync()
    {
        try
        {
            await migrator.MigrateAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "The database could not be brought up to date after the failed restore");
        }
    }

    /// <summary>Whether the backup's users (in <paramref name="model"/>'s shape) hold an enabled local administrator.</summary>
    private static bool BackupHoldsAdministrator(IModel model, string tables) =>
        UsersOf(model, tables).Any(u => u.Local && u.Admin && !u.Disabled);

    private static bool BackupHoldsLocalUser(IModel model, string tables, string subject) =>
        UsersOf(model, tables).Any(u => u.Local && u.Subject == subject);

    /// <summary>The backup's users as the Standalone rule needs them, read from its rows by the columns <paramref name="model"/> names; none before there were users.</summary>
    private static IEnumerable<(bool Local, bool Admin, bool Disabled, string? Subject)> UsersOf(IModel model, string tables)
    {
        var users = model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == "Users");
        var file = Path.Combine(tables, "Users.jsonl");
        if (users is null || !File.Exists(file)) yield break;
        var table = StoreObjectIdentifier.Table("Users", users.GetSchema());
        string Column(string property) => users.FindProperty(property)?.GetColumnName(table) ?? property;
        var (provider, admin, disabled, subject) = (Column("Provider"), Column("IsAdmin"), Column("IsDisabled"), Column("Subject"));
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0) continue;
            using var row = JsonDocument.Parse(line);
            var r = row.RootElement;
            yield return (
                r.TryGetProperty(provider, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() == nameof(UserProvider.Local),
                IsTrue(r, admin), IsTrue(r, disabled),
                r.TryGetProperty(subject, out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null);
        }
    }

    /// <summary>A flag as a dump holds it: a boolean, or a number where the provider keeps one.</summary>
    private static bool IsTrue(JsonElement row, string column) =>
        row.TryGetProperty(column, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetInt64() != 0));

    /// <summary>
    /// Standalone needs an administrator to sign in with afterwards: among the users the backup brings, or one the start creates from the
    /// bootstrap credentials as <c>StandaloneBootstrapper</c> will try to (it fails the start otherwise, and every start after it). Asked of
    /// the loaded rows inside the load's transaction, so a backup without one is refused, never applied.
    /// </summary>
    private Task RequireAdministratorAsync(AppDbContext db, CancellationToken ct) =>
        RequireAdministratorAsync(
            () => db.Users.AnyAsync(u => u.Provider == UserProvider.Local && u.IsAdmin && !u.IsDisabled, ct),
            address => db.Users.AnyAsync(u => u.Provider == UserProvider.Local && u.Subject == address, ct));

    /// <summary>The rule itself, over what the rows say: an enabled local administrator, or bootstrap credentials of an address no local account has.</summary>
    private async Task RequireAdministratorAsync(Func<Task<bool>> hasAdministrator, Func<string, Task<bool>> localAccountExists)
    {
        if (auth.Value.Mode != AuthMode.Standalone || await hasAdministrator()) return;
        var bootstrap = auth.Value.Standalone;
        var reason = StandaloneBootstrap.ProblemOf(bootstrap.AdminEmail, bootstrap.AdminPassword, out var address)
            ?? (await localAccountExists(address!) ? "taken" : null);
        if (reason is not null)
            throw new DomainException("settings.standaloneNeedsAdministrator",
                "The backup holds no administrator to sign in with in Standalone mode, and none can be created from Auth:Standalone:AdminEmail and Auth:Standalone:AdminPassword.",
                new { Reason = reason });
    }

    private RestoreOutcome Refuse(string markerPath, RestoreMarker? marker, Exception e, string reason)
    {
        logger.LogError(e, "The restore is refused ({Reason}); the start goes on with the data as it is", reason);
        Rename(markerPath, marker, "restore.refused");
        return RestoreOutcome.Refused;
    }

    /// <summary>
    /// The marker kept as <c>&lt;stem&gt;.&lt;time&gt;</c>, for the operator to see what was asked for; without the identity it carried, which
    /// opens every backup of the folder (a marker that could not be read is moved as it is).
    /// </summary>
    private void Rename(string markerPath, RestoreMarker? marker, string stem)
    {
        var target = Path.Combine(Path.GetDirectoryName(markerPath)!, string.Create(CultureInfo.InvariantCulture, $"{stem}.{clock.GetUtcNow():yyyyMMdd'T'HHmmss'Z'}"));
        try
        {
            if (marker is null) File.Move(markerPath, target, overwrite: true);
            else
            {
                (marker with { Identity = null }).WriteTo(target);
                File.Delete(markerPath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "The restore marker could not be renamed; it is tried again at the next start");
        }
    }

    /// <summary>
    /// Everything inside a root replaced by the backup's (the folder itself stays: it may be a mount point); with <paramref name="keepPhotos"/>
    /// the photos of logs below the vehicles (<c>vehicles/&lt;id&gt;/refuelings|expenses</c>) stay, the drafts go like the rest. What the app
    /// may not remove (the lost+found of a disk mounted as the root, which is root's) stays, as a backup leaves it out.
    /// </summary>
    private void ReplaceContents(string root, string source, string what, bool keepPhotos = false)
    {
        Directory.CreateDirectory(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            if (keepPhotos && Directory.Exists(entry) && Path.GetFileName(entry) == ImageFolders.VehiclesFolder)
            {
                foreach (var vehicle in Directory.EnumerateDirectories(entry))
                    foreach (var below in Directory.EnumerateFileSystemEntries(vehicle).ToList())
                        if (!(Directory.Exists(below) && LogPhotoFolders.Contains(Path.GetFileName(below)))) Delete(below, what);
                continue;
            }
            Delete(entry, what);
        }
        var count = CopyContents(source, root);
        logger.LogInformation("Restored {Count} files of the {What}{Kept}", count, what, keepPhotos ? "; the photos of logs on disk stayed" : "");
    }

    /// <summary>
    /// The key files replaced by the backup's: only what a backup takes of the folder (its <c>*.xml</c> right inside it, as the writer reads
    /// them), never anything else there: the folder may be one the app keeps other things in.
    /// </summary>
    private void ReplaceKeys(string root, string source)
    {
        Directory.CreateDirectory(root);
        foreach (var key in Directory.EnumerateFiles(root, "*.xml")) Delete(key, "keys");
        var count = 0;
        if (Directory.Exists(source))
            foreach (var key in Directory.EnumerateFiles(source, "*.xml"))
            {
                File.Copy(key, Path.Combine(root, Path.GetFileName(key)), overwrite: true);
                count++;
            }
        logger.LogInformation("Restored {Count} files of the {What}{Kept}", count, "keys", "");
    }

    private void Delete(string entry, string what)
    {
        try
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
        catch (UnauthorizedAccessException e)
        {
            logger.LogWarning(e, "Something below the {What} folder that the app may not remove stays as it is", what);
        }
    }

    private static int CopyContents(string source, string target)
    {
        if (!Directory.Exists(source)) return 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            count++;
        }
        return count;
    }
}
