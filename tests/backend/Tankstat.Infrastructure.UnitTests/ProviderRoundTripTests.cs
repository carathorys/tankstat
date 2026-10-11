using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// The dump and the load against real database servers, which CI does not have: a backup of one database is loaded into another (of the
/// same provider, or of SQLite and back) and comes out of that one's backup the same. This is what found a SQL Server that refuses
/// snapshots by default, a MySQL connector that cannot read a <c>date</c> as a <c>DateOnly</c>, and migrations that carry another timestamp on
/// every provider. A provider runs when a connection string for it is in the environment, <c>{db}</c> standing for a database name that
/// is made for the test and dropped after it:
/// <code>
/// TANKSTAT_TEST_POSTGRESQL="Host=localhost;Port=55432;Database={db};Username=postgres;Password=secret"
/// TANKSTAT_TEST_SQLSERVER="Server=localhost,51433;Database={db};User Id=sa;Password=...;TrustServerCertificate=True;Encrypt=False"
/// TANKSTAT_TEST_MYSQL="Server=localhost;Port=53306;Database={db};User=root;Password=secret"
/// </code>
/// Without it a provider's tests pass and say so (docs/development.md has the docker lines).
/// </summary>
public sealed class ProviderRoundTripTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-providers-{Guid.NewGuid():N}");
    private readonly List<string> _folders = [];
    private readonly List<TestDatabase> _servers = [];

    public static TheoryData<string> Servers => new("PostgreSql", "SqlServer", "MySql");

    private static string? Template(string provider) => Environment.GetEnvironmentVariable($"TANKSTAT_TEST_{provider.ToUpperInvariant()}");

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            using (var context = server.Get<IDbContextFactory<AppDbContext>>().CreateDbContext()) context.Database.EnsureDeleted();
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        foreach (var folder in _folders.Concat([_data]).Where(Directory.Exists)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>A migrated database of the provider: the SQLite file of every test, or a new database on the server the environment names.</summary>
    private TestDatabase Database(string provider, string name)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Storage:Path"] = Path.Combine(_data, name, "uploads"),
            ["DataProtection:KeysPath"] = Path.Combine(_data, name, "keys"),
            ["Backups:Path"] = Path.Combine(_data, name, "backups"),
        };
        if (provider != "Sqlite")
        {
            settings["Database:Provider"] = provider;
            settings["Database:ConnectionString"] = Template(provider)!.Replace("{db}", $"tankstat_{name}_{Guid.NewGuid():N}"[..40]);
        }
        var database = new TestDatabase(settings);
        if (provider != "Sqlite") _servers.Add(database);
        return database;
    }

    private async Task<(BackupResult Backup, string Tables)> SeededBackup(string provider, string name)
    {
        var db = Database(provider, name);
        await BackupSeed.SeedAsync(db, Path.Combine(_data, name, "uploads"), Path.Combine(_data, name, "uploads"), Path.Combine(_data, name, "keys"));
        var backup = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return (backup, Inner(backup.Path));
    }

    private string Inner(string file)
    {
        var folder = BackupSeed.Inner(file);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>Loads the tables of a backup into a database of its own (with rows of its own, all replaced) and backs that up.</summary>
    private async Task<(BackupResult Backup, string Tables)> LoadedAndBackedUp(string provider, string name, BackupResult from, string fromTables)
    {
        var db = Database(provider, name);
        await BackupSeed.SeedAsync(db, Path.Combine(_data, name, "uploads"), Path.Combine(_data, name, "uploads"), Path.Combine(_data, name, "keys"));
        await using (var context = await db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await TableLoad.LoadAsync(context, Path.Combine(fromTables, "tables"), from.Manifest.Contents.Tables, DateTimeOffset.UtcNow, from.Manifest.CreatedAt, db.Log.For<ProviderRoundTripTests>(), default);
        var backup = await db.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return (backup, Inner(backup.Path));
    }

    private static IEnumerable<string> TableNames(BackupResult backup) => backup.Manifest.Contents.Tables.Keys.Where(t => t is not ("Instance" or TableDump.MigrationsHistory));

    [Theory]
    [MemberData(nameof(Servers))]
    public async Task ABackupOfAServerDatabase_LoadsIntoAnother_AndComesOutOfItsBackupLineForLine(string provider)
    {
        if (Template(provider) is null) { output.WriteLine($"Skipped: TANKSTAT_TEST_{provider.ToUpperInvariant()} is not set."); return; }
        var (a, aTables) = await SeededBackup(provider, "a");

        var (b, bTables) = await LoadedAndBackedUp(provider, "b", a, aTables);

        foreach (var table in TableNames(a)) Assert.Equal(BackupSeed.Lines(aTables, table).Order(), BackupSeed.Lines(bTables, table).Order());
        Assert.Equal(a.Manifest.Contents.Tables.Where(t => t.Key != "Instance"), b.Manifest.Contents.Tables.Where(t => t.Key != "Instance"));
        Assert.True(a.Manifest.Contents.Tables.Values.Sum() > 20);
    }

    [Theory]
    [MemberData(nameof(Servers))]
    public async Task ABackupMovesFromSqliteToAServer_AndBack_WithTheSameValues(string provider)
    {
        if (Template(provider) is null) { output.WriteLine($"Skipped: TANKSTAT_TEST_{provider.ToUpperInvariant()} is not set."); return; }
        var (fromSqlite, fromTables) = await SeededBackup("Sqlite", "a");

        var (onServer, serverTables) = await LoadedAndBackedUp(provider, "b", fromSqlite, fromTables);
        foreach (var table in TableNames(fromSqlite)) Assert.Equal(Normalised(fromTables, table), Normalised(serverTables, table));

        var (backInSqlite, backTables) = await LoadedAndBackedUp("Sqlite", "c", onServer, serverTables);
        foreach (var table in TableNames(fromSqlite)) Assert.Equal(Normalised(fromTables, table), Normalised(backTables, table));
        Assert.Equal(fromSqlite.Manifest.Contents.Tables.Where(t => t.Key != "Instance"), backInSqlite.Manifest.Contents.Tables.Where(t => t.Key != "Instance"));
    }

    private static readonly Regex Timestamp = new(@"^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(\.\d+)?Z$");

    /// <summary>
    /// The rows of a table as values that can be compared between providers: numbers without trailing zeros (40 and 40.00), timestamps to the
    /// microsecond (PostgreSQL and MySQL keep no more than that, SQLite and SQL Server a tenth of it), ids in lower case.
    /// </summary>
    private static List<string> Normalised(string tables, string table) => [.. BackupSeed.Lines(tables, table).Select(line =>
    {
        using var row = JsonDocument.Parse(line);
        return string.Join(",", row.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"{p.Name}={Value(p.Value)}"));
    }).Order(StringComparer.Ordinal)];

    private static string Value(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.GetDecimal().ToString("0.#############################", CultureInfo.InvariantCulture);
        if (value.ValueKind != JsonValueKind.String) return value.ToString();
        var text = value.GetString()!;
        if (Timestamp.Match(text) is { Success: true } m) return $"{m.Groups[1].Value}.{(m.Groups[2].Success ? m.Groups[2].Value[1..] : "0").PadRight(7, '0')[..6]}Z";
        return Guid.TryParse(text, out var id) ? id.ToString("D") : text;
    }
}
