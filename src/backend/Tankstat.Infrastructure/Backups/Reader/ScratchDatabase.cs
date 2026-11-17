using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>
/// A backup's cars in a throwaway SQLite file (#336), whatever database wrote the backup and at whichever migration this release knows:
/// set up at the backup's migration, loaded with that migration's model, then brought up to date by this release's migrations, as a
/// restore does. It is never the app's database and never reached through the app's factory: a context of its own, unpooled (so the
/// file can be deleted), with foreign keys off (the tables left out would break them), without the sync interceptor, and read-only
/// once loaded.
/// </summary>
internal sealed class ScratchDatabase(string file) : IAsyncDisposable
{
    public const string SqliteMigrations = "Tankstat.Migrations.Sqlite";

    public string File { get; } = file;

    private static DbContextOptions<AppDbContext> Options(string file, bool readOnly) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={file};Pooling=False;Foreign Keys=False{(readOnly ? ";Mode=ReadOnly" : "")}", o => o.MigrationsAssembly(SqliteMigrations))
            .AddInterceptors(SqliteUnicodeLower.Instance)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

    /// <summary>A context for building: nothing is opened until it is used.</summary>
    public static AppDbContext Builder(string file) => new(Options(file, readOnly: false));

    /// <summary>A read-only context on the loaded database; the caller disposes it.</summary>
    public AppDbContext Read() => new(Options(File, readOnly: true));

    /// <summary>
    /// This release's id of the backup's migration; a backup from a migration it does not know was written by a newer release
    /// (<c>backup.newerFormat</c>: the advice of <c>backup.otherMigration</c>, to restore it, does not fit a reader).
    /// </summary>
    public static string MigrationOf(AppDbContext db, BackupManifest manifest) =>
        MigrationModels.IdOf(db, manifest.Migration)
        ?? throw new DomainException("backup.newerFormat", "The backup was written by a newer release.", new { manifest.Format, AppVersion = BackupReaderText.Version(manifest.AppVersion) });

    /// <summary>
    /// Sets the file up at <paramref name="migration"/>, loads the rows of <paramref name="tablesFolder"/> (their counts as
    /// <paramref name="expected"/>) and brings it up to date. No line is logged: a table's name in a file is the uploader's text.
    /// </summary>
    public static async Task<ScratchDatabase> LoadAsync(string file, string migration, string tablesFolder, IReadOnlyDictionary<string, long> expected, CancellationToken ct)
    {
        await using (var db = Builder(file))
        {
            await db.GetService<IMigrator>().MigrateAsync(migration, ct);
            await TableLoad.LoadRowsAsync(db, MigrationModels.ModelOf(db, migration), tablesFolder, expected, NullLogger.Instance, ct);
            await db.Database.MigrateAsync(ct);
        }
        return new ScratchDatabase(file);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var path in new[] { File, File + "-wal", File + "-shm", File + "-journal" })
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Text a file says about itself, cut to what may be shown: it is the uploader's.</summary>
internal static class BackupReaderText
{
    public const int MaxVersionLength = 40;

    /// <summary>The release a manifest names, at most <see cref="MaxVersionLength"/> characters of letters, digits, dots, dashes and pluses.</summary>
    public static string Version(string? text) =>
        new([.. (text ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+').Take(MaxVersionLength)]);
}
