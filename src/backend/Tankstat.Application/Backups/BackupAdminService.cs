using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Images;

namespace Tankstat.Application.Backups;

/// <summary>
/// What the administration page shows of the backups at no cost: the folder, the run under way or the last one, the settings and when the
/// schedule writes the next one (null: nothing scheduled). Polled while a backup runs.
/// </summary>
public sealed record BackupStatus(string Folder, BackupRunStatus Run, BackupOptions Settings, bool PhotosApart, DateTimeOffset? NextRun);

/// <summary>The backups in the folder and the migration the database is at, which a backup must match to be restored here.</summary>
public sealed record RestorePoints(IReadOnlyList<RestorePoint> Points, string? DatabaseMigration);

/// <summary>Bytes of the database (null: the provider would not say) and of the uploaded files.</summary>
public sealed record StorageSizes(long? Database, FileUsage Files);

/// <summary>
/// The backups for administrators: the status, the restore points in the folder and the sizes (each on its own, so polling the status costs
/// nothing), a backup written now, a file uploaded or downloaded, and a key set made
/// for encrypted backups. Restoring one is <see cref="RestoreService"/>. Every call needs an administrator (so none of it exists with
/// sign-in off, where the command line is the way); the file names and sizes are logged, never a passphrase or an identity.
/// </summary>
public sealed class BackupAdminService(
    AccessService access,
    IBackupStore store,
    IBackupRunner runner,
    IDatabaseFacts database,
    IStorageUsage usage,
    IBackupKeyMaker keys,
    IOptionsSnapshot<BackupOptions> options,
    IOptions<StorageOptions> storage,
    TimeProvider clock,
    ILogger<BackupAdminService> logger)
{
    public async Task<BackupStatus> StatusAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        var o = options.Value;
        var next = o.DailyAt is { } at ? BackupSchedule.NextAfter(clock.GetUtcNow(), at, clock.LocalTimeZone) : (DateTimeOffset?)null;
        return new BackupStatus(store.Folder, runner.Status, o, !storage.Value.OneRoot, next);
    }

    /// <summary>Every backup in the folder (each file's manifest is read), so only asked for when shown.</summary>
    public async Task<RestorePoints> RestorePointsAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return new RestorePoints(store.List(), await database.LastMigrationAsync(ct));
    }

    /// <summary>The migration the database is at (a question to the database only, the folder is not read).</summary>
    public async Task<string?> MigrationAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        return await database.LastMigrationAsync(ct);
    }

    /// <summary>The sizes: a question to the database and a walk of the upload folders, side by side, so only asked for when shown.</summary>
    public async Task<StorageSizes> SizesAsync(CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        var size = database.SizeAsync(ct);
        var files = usage.MeasureAsync(ct);
        await Task.WhenAll(size, files);
        return new StorageSizes(await size, await files);
    }

    /// <summary>Starts a backup now; false when one is being written already (that one is the backup asked for).</summary>
    public async Task<bool> RunNowAsync(CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var started = runner.Start(admin.Id);
        if (started) logger.LogInformation("A backup was started by user {UserId}", admin.Id);
        return started;
    }

    /// <summary>A key set for encrypted backups (the passphrase given, or one made up), shown to the administrator once.</summary>
    public async Task<BackupKeySet> PrepareEncryptionAsync(string? passphrase, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var set = keys.Make(passphrase);
        logger.LogInformation("User {UserId} made a key set for encrypted backups", admin.Id);
        return set;
    }

    public async Task<RestorePoint> UploadAsync(Stream content, string? fileName, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var point = await store.SaveUploadAsync(content, fileName, options.Value.MaxUploadBytes, ct);
        logger.LogInformation("User {UserId} uploaded the backup {File} ({Bytes} bytes)", admin.Id, point.File, point.Bytes);
        return point;
    }

    public async Task<Stream> DownloadAsync(string fileName, CancellationToken ct)
    {
        var admin = await access.RequireAdminAsync(ct);
        var stream = store.OpenRead(fileName);
        logger.LogInformation("User {UserId} downloaded the backup {File}", admin.Id, fileName);
        return stream;
    }

    /// <summary>
    /// Whether a backup can be downloaded (<c>backup.notFound</c> otherwise). The page asks before it hands the download to the browser, so a
    /// refusal is said on the page instead of replacing it; nothing is read or logged (the download is).
    /// </summary>
    public async Task CheckDownloadAsync(string fileName, CancellationToken ct)
    {
        await access.RequireAdminAsync(ct);
        await using var _ = store.OpenRead(fileName);
    }
}
