using Tankstat.Application.Backups;

namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>
/// From a backup file to its cars in a scratch database (#336), in a folder of the caller's: the inner archive in plain
/// (<see cref="ArchiveName"/>, kept), the loaded tables' rows (removed once loaded) and the database (<see cref="DatabaseName"/>).
/// </summary>
internal static class BackupScratchLoad
{
    public const string ArchiveName = "data.zip";
    public const string DatabaseName = "cars.db";
    private const string TablesFolder = "tables";

    /// <param name="folder">An empty folder of this load's own; whoever asked removes it, also when the load fails.</param>
    /// <param name="keepEmails">Whether the backup's users keep their e-mail addresses (an administrator's partial restore); else only their names.</param>
    public static async Task<ScratchDatabase> RunAsync(
        string path, BackupManifest manifest, byte[]? identity, string folder, UnpackLimits limits, bool keepEmails, CancellationToken ct)
    {
        var file = Path.Combine(folder, DatabaseName);
        string migration;
        Microsoft.EntityFrameworkCore.Metadata.IModel model;
        await using (var builder = ScratchDatabase.Builder(file))
        {
            migration = ScratchDatabase.MigrationOf(builder, manifest); // refused before anything is unpacked
            model = MigrationModels.ModelOf(builder, migration);
        }
        var archive = Path.Combine(folder, ArchiveName);
        await BackupUnpack.InnerArchiveAsync(path, manifest, identity, archive, limits.ArchiveBytes, ct);
        var tables = Path.Combine(folder, TablesFolder);
        try
        {
            var expected = await BackupUnpack.TablesAsync(archive, model, manifest, tables, limits, keepEmails, ct);
            return await ScratchDatabase.LoadAsync(file, migration, tables, expected, ct);
        }
        finally
        {
            if (Directory.Exists(tables)) Directory.Delete(tables, recursive: true); // the rows are in the database now, or the load failed
        }
    }
}
