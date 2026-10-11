using Microsoft.Extensions.Options;
using Tankstat.Application;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Backups;

namespace Tankstat.Infrastructure.Backups;

/// <summary>The backups folder (<c>Backups:Path</c>) for <see cref="RestoreService"/>: files by bare name only, so nothing outside it is ever reached.</summary>
internal sealed class FileBackupStore(IOptions<BackupOptions> options) : IBackupStore
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
        if (!BackupKeys.Opens(key, encryption.Recipient)) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        return AgeKeys.FormatIdentity(key);
    }

    public bool RestorePending => File.Exists(RestoreMarker.PathIn(Root));

    public void RequestRestore(RestoreMarker marker) => marker.Write(Root);

    private static NotFoundException NotFound() => new("backup.notFound", "There is no such backup in the backups folder.");
}
