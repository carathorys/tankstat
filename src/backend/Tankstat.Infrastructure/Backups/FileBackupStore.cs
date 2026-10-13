using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>The backups folder (<c>Backups:Path</c>) for <see cref="RestoreService"/>: files by bare name only, so nothing outside it is ever reached.</summary>
internal sealed class FileBackupStore(IOptions<BackupOptions> options, IDbContextFactory<AppDbContext> dbFactory) : IBackupStore
{
    private string Root => options.Value.Root;

    public BackupManifest ReadManifest(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName.StartsWith('.')) throw NotFound();
        var path = Path.Combine(Root, fileName);
        if (!File.Exists(path)) throw NotFound();
        return BackupFile.ReadManifest(path);
    }

    public string? IdentityFor(BackupManifest manifest, string? passphrase, string? identity)
    {
        if (manifest.Encryption is not { } encryption) return null;
        byte[] key;
        if (!string.IsNullOrEmpty(identity))
        {
            try
            {
                key = AgeKeys.ParseIdentity(identity);
            }
            catch (FormatException)
            {
                throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
            }
        }
        else if (!string.IsNullOrEmpty(passphrase)) key = BackupFile.IdentityOf(manifest, passphrase);
        else throw new DomainException("backup.keyRequired", "The backup is encrypted: its passphrase or identity is needed.");
        bool opens;
        try
        {
            opens = BackupKeys.Opens(key, encryption.Recipient);
        }
        catch (FormatException)
        {
            throw new DomainException("backup.corrupt", "The file is damaged or is not a Tankstat backup."); // a manifest whose recipient is none
        }
        if (!opens) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        return AgeKeys.FormatIdentity(key);
    }

    public Task RequireSameMigrationAsync(BackupManifest manifest, CancellationToken ct) => RequireSameMigrationAsync(dbFactory, manifest, ct);

    /// <summary>
    /// The backup's last migration must be the one the database is at, compared by name: each provider stamps the same migration with its
    /// own timestamp, and a backup may come from another provider. <c>backup.otherMigration</c> otherwise (older backups wait for a replay).
    /// </summary>
    internal static async Task RequireSameMigrationAsync(IDbContextFactory<AppDbContext> dbFactory, BackupManifest manifest, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
        if (!MigrationNames.Same(applied, manifest.Migration))
            throw new DomainException("backup.otherMigration", "The backup was made at another migration than the database is at.", new { Backup = manifest.Migration, Database = applied });
    }

    public bool RestorePending => File.Exists(RestoreMarker.PathIn(Root));

    public void RequestRestore(RestoreMarker marker) => marker.Write(Root);

    private static NotFoundException NotFound() => new("backup.notFound", "There is no such backup in the backups folder.");
}
