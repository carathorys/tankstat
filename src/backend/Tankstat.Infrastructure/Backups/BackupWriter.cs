using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Health;
using Tankstat.Application.Images;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Writes a backup: an outer zip (stored) holding the inner archive, <c>data.age</c> (encrypted for the recipient) or <c>data.zip</c>
/// (plain), and after it <c>manifest.json</c> in plain, which says what the file holds (sizes, row counts and the checksum are only known
/// once the inner archive is written, so the manifest comes last and is found by name). The inner zip holds <c>tables/&lt;Table&gt;.jsonl</c>
/// from one consistent read (<see cref="BackupSource"/>), <c>files/pictures/...</c> (everything under <c>Storage:Path</c> but the drafts),
/// <c>files/photos/...</c> (a photos root of its own, only when asked for) and, in an encrypted backup only, <c>keys/*.xml</c> (the Data
/// Protection keys, which are secrets in plain). Everything is streamed, the file is written under a temporary name and renamed when
/// complete, so the folder never holds a half backup (and never the database's plain copy, see <see cref="BackupSource"/>).
/// </summary>
internal sealed class BackupWriter(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<DatabaseOptions> database,
    IOptionsMonitor<BackupOptions> options,
    IOptions<StorageOptions> storage,
    IOptions<KeyRingOptions> keyRing,
    TimeProvider clock,
    ILogger<BackupWriter> logger) : IBackupWriter
{
    private const string Partial = ".partial";

    public async Task<BackupResult> WriteAsync(BackupRequest request, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var roots = storage.Value;
        var createdAt = clock.GetUtcNow();
        var encryption = request.Encryption ?? Configured(o);
        var photos = roots.OneRoot ? BackupPhotos.WithPictures : (request.Photos ?? o.Photos) ? BackupPhotos.Separate : BackupPhotos.LeftOut;
        var folder = Path.GetDirectoryName(Path.GetFullPath(request.Output ?? Path.Combine(o.Root, "x")))!;
        Directory.CreateDirectory(folder);
        var output = request.Output is null ? Unused(folder, $"tankstat-backup-{createdAt:yyyyMMdd'T'HHmmss'Z'}") : Path.GetFullPath(request.Output);
        var partial = output + Partial;
        try
        {
            var manifest = await WriteAsync(partial, createdAt, encryption, photos, request.SafetyNet, ct);
            File.Move(partial, output);
            logger.LogInformation("Wrote backup {File}: {Tables} tables with {Rows} rows, {Bytes} bytes of files, {Encryption}",
                Path.GetFileName(output), manifest.Contents.Tables.Count, manifest.Contents.Tables.Values.Sum(), manifest.Sizes.Pictures + manifest.Sizes.Photos,
                encryption is null ? "in plain" : "encrypted");
            return new BackupResult(output, manifest);
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    private async Task<BackupManifest> WriteAsync(string partial, DateTimeOffset createdAt, BackupEncryption? encryption, BackupPhotos photos, bool safetyNet, CancellationToken ct)
    {
        var roots = storage.Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var migration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
        var tables = TableDump.Tables(db.Model);
        var sql = db.GetService<ISqlGenerationHelper>();
        await using var source = await BackupSource.OpenAsync(db, database.Value.Provider, logger, ct);

        var rows = new Dictionary<string, long>();
        long tableBytes = 0, pictureBytes = 0, photoBytes = 0, keyBytes = 0;
        var keys = encryption is not null;
        string archiveName;
        HashingStream hashing;
        await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var outer = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            archiveName = encryption is null ? BackupArchive.Plain : BackupArchive.Encrypted;
            hashing = new HashingStream(outer.CreateEntry(archiveName, CompressionLevel.NoCompression).Open());
            var data = encryption is null ? hashing : AgeStream.Encrypt(hashing, AgeKeys.ParseRecipient(encryption.Recipient));
            using (var inner = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var table in tables)
                {
                    await using var entry = inner.CreateEntry($"tables/{table.Name}.jsonl", CompressionLevel.Optimal).Open();
                    var (count, bytes) = await TableDump.WriteAsync(source, sql, table, entry, ct);
                    rows[table.Name] = count;
                    tableBytes += bytes;
                }
                pictureBytes = await AddFilesAsync(inner, "files/pictures", roots.PicturesRoot, ct);
                if (photos == BackupPhotos.Separate) photoBytes = await AddFilesAsync(inner, "files/photos", roots.PhotosRoot, ct);
                if (keys) keyBytes = await AddKeysAsync(inner, ct);
            }
            var manifest = new BackupManifest(
                BackupManifest.CurrentFormat, BackupManifest.Tankstat, HealthReporter.AppVersion, createdAt, database.Value.Provider.ToString(), migration, safetyNet,
                new BackupContents(rows, Pictures: true, photos, keys),
                new BackupSizes(tableBytes, pictureBytes, photoBytes, keyBytes, hashing.Written),
                new BackupArchive(archiveName, hashing.Sha256Hex),
                encryption);
            await using (var entry = outer.CreateEntry(BackupManifest.FileName, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(Encoding.UTF8.GetBytes(manifest.ToJson()), ct);
            return manifest;
        }
    }

    /// <summary>Adds the files of a root below a folder of the archive; returns their bytes. A file gone meanwhile (a purged vehicle) is left out.</summary>
    private async Task<long> AddFilesAsync(ZipArchive archive, string prefix, string root, CancellationToken ct)
    {
        long bytes = 0;
        foreach (var (relative, full) in BackupFiles.Walk(root))
        {
            FileStream input;
            try
            {
                input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                logger.LogDebug("A file disappeared while the backup was written and is not in it");
                continue;
            }
            await using (input)
            await using (var entry = archive.CreateEntry($"{prefix}/{relative}", CompressionLevel.NoCompression).Open())
            {
                await input.CopyToAsync(entry, ct);
                bytes += input.Length;
            }
        }
        return bytes;
    }

    /// <summary>The Data Protection key files, directly in the keys folder.</summary>
    private async Task<long> AddKeysAsync(ZipArchive archive, CancellationToken ct)
    {
        long bytes = 0;
        var root = keyRing.Value.Root;
        if (!Directory.Exists(root)) return 0;
        foreach (var full in Directory.EnumerateFiles(root, "*.xml").OrderBy(f => f, StringComparer.Ordinal))
        {
            await using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var entry = archive.CreateEntry($"keys/{Path.GetFileName(full)}", CompressionLevel.Optimal).Open();
            await input.CopyToAsync(entry, ct);
            bytes += input.Length;
        }
        return bytes;
    }

    /// <summary>The dated name, or the next free one when a backup was written in the same second (a run by hand next to a scheduled one).</summary>
    private static string Unused(string folder, string stem)
    {
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(folder, n == 1 ? $"{stem}.zip" : $"{stem}-{n}.zip");
            if (!File.Exists(path) && !File.Exists(path + Partial)) return path;
        }
    }

    private static BackupEncryption? Configured(BackupOptions o) =>
        o.Encrypted ? new BackupEncryption(BackupEncryption.AgeX25519, o.Encryption.Recipient!.Trim(), o.Encryption.Salt!.Trim(), Argon2idParameters.Default) : null;
}
