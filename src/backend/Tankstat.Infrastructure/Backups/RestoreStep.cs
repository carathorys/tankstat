using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Images;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// At start, after the database migrations and before anything else touches the data: applies a restore that waits in the backups
/// folder (<see cref="RestoreMarker"/>), and gives a fresh instance its generation; <see cref="InstanceState"/> then knows which generation
/// the data has, for every session answer.
/// <para>
/// A restore replaces the instance's data as a whole, never merges: the manifest and the checksum are checked, the backup's migration must
/// be the one the database is at (this release's), an encrypted backup's identity must open it; then a backup of the state about to be
/// replaced is written first (the safety net; without it nothing goes on), the file is unpacked into a temporary folder, every table is
/// loaded in one transaction (<see cref="TableLoad"/>, which stamps the new generation last), the contents of the pictures root (and of the
/// photos root, when the backup holds it) and of the keys folder (an encrypted backup) are replaced, and the marker goes. A refused
/// restore renames the marker to <c>restore.refused.&lt;time&gt;</c> and the start goes on with the data as it is; a failure after the
/// database was replaced (the files half copied) renames it to <c>restore.failed.&lt;time&gt;</c>, and the safety net is the way back. Nothing
/// here ever stops the app.
/// </para>
/// </summary>
internal sealed class RestoreStep(
    IDbContextFactory<AppDbContext> dbFactory,
    IBackupWriter writer,
    IOptions<BackupOptions> options,
    IOptions<StorageOptions> storage,
    IOptions<KeyRingOptions> keyRing,
    IOptions<AuthOptions> auth,
    InstanceState state,
    RestoreCode code,
    TimeProvider clock,
    ILogger<RestoreStep> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var marker = RestoreMarker.PathIn(options.Value.Root);
        if (File.Exists(marker)) await RestoreAsync(marker, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var instance = await db.Instance.SingleOrDefaultAsync(ct);
        if (instance is null)
        {
            instance = Instance.Fresh();
            db.Instance.Add(instance);
            await db.SaveChangesAsync(ct);
            logger.LogDebug("The instance got its generation {Generation}", instance.Generation);
        }
        state.Set(new InstanceInfo(instance.Generation, instance.RestorePoint));

        if (auth.Value.Mode == AuthMode.None)
            logger.LogInformation("Restoring a backup from the app needs the one-time restore code {Code} (sign-in is off, so there is no administrator to ask)", code.Value);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task RestoreAsync(string markerPath, CancellationToken ct)
    {
        RestoreMarker marker;
        try
        {
            marker = RestoreMarker.Read(markerPath);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Refuse(markerPath, e, "the marker cannot be read");
            return;
        }
        var file = Path.Combine(options.Value.Root, Path.GetFileName(marker.File));
        BackupManifest manifest;
        byte[]? identity = null;
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
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
                if (!string.Equals(applied, manifest.Migration, StringComparison.Ordinal))
                    throw new DomainException("backup.otherMigration", "The backup was made at another migration than the database is at.", new { Backup = manifest.Migration, Database = applied });
            }
            BackupFile.VerifyChecksum(file, manifest);
        }
        catch (Exception e) when (e is DomainException or IOException or FormatException)
        {
            Refuse(markerPath, e, e is DomainException d ? d.Key : "the backup cannot be read");
            return;
        }

        logger.LogInformation("Restoring the backup {File} (written by Tankstat {Version} on {CreatedAt}, {Provider}), asked for by user {UserId}",
            Path.GetFileName(file), manifest.AppVersion, manifest.CreatedAt, manifest.Provider, marker.RequestedById);
        try
        {
            await writer.WriteAsync(new BackupRequest(SafetyNet: true), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Refuse(markerPath, e, "the safety-net backup of the data about to be replaced could not be written");
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("tankstat-restore-");
        var loaded = false;
        try
        {
            BackupFile.Extract(file, manifest, identity, scratch.FullName);
            InstanceInfo instance;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                instance = await TableLoad.LoadAsync(db, Path.Combine(scratch.FullName, "tables"), manifest.Contents.Tables, clock.GetUtcNow(), manifest.CreatedAt, logger, ct);
            loaded = true;
            var roots = storage.Value;
            if (manifest.Contents.Pictures) ReplaceContents(roots.PicturesRoot, Path.Combine(scratch.FullName, "files", "pictures"), "pictures");
            if (manifest.Contents.Photos == BackupPhotos.Separate)
            {
                // Their own root now too: replaced; one root now: they join the pictures just restored.
                if (roots.OneRoot) CopyContents(Path.Combine(scratch.FullName, "files", "photos"), roots.PhotosRoot);
                else ReplaceContents(roots.PhotosRoot, Path.Combine(scratch.FullName, "files", "photos"), "photos");
            }
            if (manifest.Contents.Keys) ReplaceContents(keyRing.Value.Root, Path.Combine(scratch.FullName, "keys"), "keys");
            File.Delete(markerPath);
            state.Set(instance);
            logger.LogInformation("Restored the backup {File}: the data is generation {Generation} now, taken back to {RestorePoint}", Path.GetFileName(file), instance.Generation, manifest.CreatedAt);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (loaded)
            {
                logger.LogError(e, "The restore of {File} failed after the database was replaced; the files may be half copied, the safety-net backup is the way back", Path.GetFileName(file));
                Rename(markerPath, "restore.failed");
            }
            else Refuse(markerPath, e, e is DomainException d ? d.Key : "it could not be unpacked or loaded; the database is as it was");
        }
        finally
        {
            try
            {
                scratch.Delete(recursive: true);
            }
            catch (IOException)
            {
                // the temporary folder stays; the system clears it
            }
        }
    }

    private void Refuse(string markerPath, Exception e, string reason)
    {
        logger.LogError(e, "The restore is refused ({Reason}); the start goes on with the data as it is", reason);
        Rename(markerPath, "restore.refused");
    }

    private void Rename(string markerPath, string stem)
    {
        try
        {
            File.Move(markerPath, Path.Combine(Path.GetDirectoryName(markerPath)!, $"{stem}.{clock.GetUtcNow():yyyyMMdd'T'HHmmss'Z'}"), overwrite: true);
        }
        catch (IOException e)
        {
            logger.LogError(e, "The restore marker could not be renamed; it is tried again at the next start");
        }
    }

    /// <summary>Everything inside a root replaced by the backup's (the folder itself stays: it may be a mount point).</summary>
    private void ReplaceContents(string root, string source, string what)
    {
        Directory.CreateDirectory(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
        var count = CopyContents(source, root);
        logger.LogInformation("Restored {Count} files of the {What}", count, what);
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
