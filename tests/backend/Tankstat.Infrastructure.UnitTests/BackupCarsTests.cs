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
using Tankstat.Infrastructure.Backups.Reader;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;
using Xunit.Abstractions;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// The backup reader's scratch load (#336): a backup's cars in a throwaway SQLite database, brought up to date as a restore would, with
/// only the cars' tables loaded. The oracle for an older backup is the app's own upgrade of the database the backup was taken from.
/// </summary>
public sealed class BackupCarsTests(ITestOutputHelper output) : IAsyncDisposable
{
    private static readonly UnpackLimits Roomy = new(1L << 30, 1L << 30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-reader-{Guid.NewGuid():N}");
    private readonly CapturedLog _log = new();
    private readonly List<ServiceProvider> _services = [];
    private readonly List<(string Provider, string ConnectionString)> _servers = [];
    private int _scratch;

    public async ValueTask DisposeAsync()
    {
        foreach (var (provider, connection) in _servers)
        {
            var config = Config(provider, connection, "drop");
            await using var services = new ServiceCollection().AddLogging().AddApplication(config).AddInfrastructure(config).BuildServiceProvider();
            await using var db = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }
        foreach (var services in _services) await services.DisposeAsync();
        // Only these databases' pooled connections: ClearAllPools would also close the ones of tests running in parallel.
        if (Directory.Exists(_root))
            foreach (var file in Directory.GetFiles(_root, "tankstat.db", SearchOption.AllDirectories))
                using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file}")) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private IConfiguration Config(string provider, string connection, string name) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Database:Provider"] = provider,
        ["Database:ConnectionString"] = connection,
        ["Storage:Path"] = Path.Combine(_root, name, "uploads"),
        ["DataProtection:KeysPath"] = Path.Combine(_root, name, "keys"),
        ["Backups:Path"] = Path.Combine(_root, name, "backups"),
    }).Build();

    /// <summary>The app's services on a database of the provider that is not set up yet: a SQLite file, or a new database on the server the environment names (null without one).</summary>
    private ServiceProvider? Instance(string provider, string name)
    {
        Directory.CreateDirectory(Path.Combine(_root, name));
        var connection = $"Data Source={Path.Combine(_root, name, "tankstat.db")}";
        if (provider != "Sqlite")
        {
            if (Environment.GetEnvironmentVariable($"TANKSTAT_TEST_{provider.ToUpperInvariant()}") is not { Length: > 0 } template)
            {
                output.WriteLine($"{provider}: not run, TANKSTAT_TEST_{provider.ToUpperInvariant()} is not set.");
                return null;
            }
            connection = template.Replace("{db}", $"tankstat_reader_{Guid.NewGuid():N}"[..40]);
            _servers.Add((provider, connection));
        }
        var config = Config(provider, connection, name);
        var services = new ServiceCollection().AddLogging(b => b.AddProvider(_log)).AddApplication(config).AddInfrastructure(config).BuildServiceProvider();
        _services.Add(services);
        return services;
    }

    private static async Task<AppDbContext> Db(ServiceProvider services) => await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    private static async Task AtMigration(ServiceProvider services, string name)
    {
        await using var db = await Db(services);
        await db.GetService<IMigrator>().MigrateAsync(MigrationModels.IdOf(db, name)!);
    }

    /// <summary>The app's own start: the safety-net backup of the database as it is, then the upgrade. Returns that backup.</summary>
    private async Task<string> StartAsync(ServiceProvider services, string name)
    {
        await services.GetServices<IHostedService>().OfType<DatabaseMigrator>().Single().StartAsync(default);
        return Directory.GetFiles(Path.Combine(_root, name, "backups"), "*.zip").Single();
    }

    private async Task<ScratchDatabase> ReadAsync(string backup, bool keepEmails = false)
    {
        var folder = Path.Combine(_root, $"scratch-{++_scratch}");
        Directory.CreateDirectory(folder);
        return await BackupScratchLoad.RunAsync(backup, BackupFile.ReadManifest(backup), null, folder, Roomy, keepEmails, default);
    }

    /// <summary>
    /// A row into a table as the migration <paramref name="model"/> has it, on any provider: the given columns, and for every other one
    /// that must hold a value a stand-in of its type. The names are quoted and the values typed by the provider's own helpers.
    /// </summary>
    private static async Task InsertAsync(AppDbContext db, Microsoft.EntityFrameworkCore.Metadata.IModel model, string table, Dictionary<string, object?> values)
    {
        var sql = db.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>();
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, null);
        var properties = model.GetEntityTypes().Where(e => e.GetTableName() == table).SelectMany(e => e.GetProperties()).ToList();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        var (names, parameters) = (new List<string>(), new List<string>());
        foreach (var column in TableDump.Tables(model).Single(t => t.Name == table).Columns)
        {
            var mapping = column.Mapping!;
            var type = Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType;
            object? value;
            if (!values.TryGetValue(column.Name, out value))
            {
                if (properties.Where(p => p.GetColumnName(store) == column.Name).All(p => p.IsColumnNullable(store))) continue;
                value = type == typeof(string) ? "" : type.IsEnum ? Enum.GetValues(type).GetValue(0) : Activator.CreateInstance(type);
            }
            if (value is DateTimeOffset at && type == typeof(DateTime)) value = at.UtcDateTime; // a migration's model keeps the column's own type
            var name = sql.GenerateParameterName($"p{names.Count}");
            names.Add(sql.DelimitIdentifier(column.Name));
            parameters.Add(name);
            command.Parameters.Add(mapping.CreateParameter(command, name, value, nullable: value is null ? true : null));
        }
        command.CommandText = $"INSERT INTO {sql.DelimitIdentifier(table)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", parameters)})";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A car with a tyre set and its "stretch" as v0.21.0-preview-1 kept them, and rows in tables the reader never loads.</summary>
    private static async Task SeedBeforeTheRenameAsync(ServiceProvider services, Guid vehicle, Guid owner, Guid set, Guid stretch)
    {
        await using var db = await Db(services);
        var model = MigrationModels.ModelOf(db, MigrationModels.IdOf(db, "AddTyres")!);
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        await InsertAsync(db, model, "Users", new() { ["Id"] = owner, ["Email"] = "owner@example.com", ["DisplayName"] = "Owner" });
        await InsertAsync(db, model, "Vehicles", new() { ["Id"] = vehicle, ["OwnerId"] = owner, ["Name"] = "Octavia", ["Version"] = 1 });
        await InsertAsync(db, model, "TyreSets", new()
        {
            ["Id"] = set, ["OwnerId"] = owner, ["VehicleId"] = vehicle, ["CreatedById"] = owner, ["Name"] = "Michelin", ["Since"] = new DateOnly(2023, 3, 1), ["Version"] = 1,
        });
        await InsertAsync(db, model, "TyreStretches", new()
        {
            ["Id"] = stretch, ["OwnerId"] = owner, ["VehicleId"] = vehicle, ["SetId"] = set, ["CreatedById"] = owner, ["From"] = new DateOnly(2026, 3, 28), ["PairOneFront"] = true, ["Version"] = 1,
        });
        await InsertAsync(db, model, "UiSettings", new() { ["UserId"] = owner, ["UpdatedAt"] = now });
        await InsertAsync(db, model, "Tombstones", new() { ["Id"] = Guid.NewGuid(), ["EntityId"] = Guid.NewGuid(), ["EntityType"] = "TyreStretch", ["PurgedAt"] = now, ["VehicleId"] = vehicle });
    }

    private sealed record TyreRows(string Installations, string Entries);

    private static async Task<TyreRows> TyreRowsAsync(AppDbContext db) => new(
        string.Join(" | ", (await db.TyreInstallations.IgnoreQueryFilters().AsNoTracking().OrderBy(i => i.Id).ToListAsync())
            .Select(i => $"{i.Id} set {i.SetId} car {i.VehicleId} {i.From:yyyy-MM-dd}..{i.Until:yyyy-MM-dd} front {i.PairOneFront}")),
        string.Join(" | ", (await db.TyreEntries.IgnoreQueryFilters().AsNoTracking().OrderBy(e => e.Day).ThenBy(e => e.Kind).ToListAsync())
            .Select(e => $"{e.Kind} set {e.SetId} car {e.VehicleId} {e.Day:yyyy-MM-dd} installation {e.InstallationId}")));

    public static TheoryData<string> Providers => new("Sqlite", "PostgreSql", "SqlServer", "MySql");

    /// <summary>
    /// #334's first criterion for the reader: a backup written at an older migration, by any of the four databases, read into the scratch
    /// SQLite, is what the database it came from became by its own upgrade. The backup is from before the two migrations that rewrite
    /// the tyre rows (the rename of the stretches, the tyre log's lines made from the days).
    /// </summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ABackupFromBeforeTheTyreRewrites_ReadsAsItsOwnDatabaseBecameByUpgrading(string provider)
    {
        if (Instance(provider, "old") is not { } services) return;
        var (vehicle, owner, set, stretch) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await AtMigration(services, "AddTyres");
        await SeedBeforeTheRenameAsync(services, vehicle, owner, set, stretch);

        var backup = await StartAsync(services, "old"); // backs up at AddTyres, then upgrades: the oracle
        await using var read = await ReadAsync(backup);

        await using var upgraded = await Db(services);
        await using var scratch = read.Read();
        var found = await TyreRowsAsync(scratch);
        Assert.Contains($"{stretch} set {set} car {vehicle} 2026-03-28", found.Installations);
        Assert.Contains($"Swap set {set} car {vehicle} 2026-03-28 installation {stretch}", found.Entries); // the line the upgrade makes for the stretch
        // The oracle: the database the backup was taken from, as its own upgrade left it. (Not on MySQL, which cannot read a date column: #345.)
        if (provider != "MySql") Assert.Equal(await TyreRowsAsync(upgraded), found);
        Assert.Equal("Owner", (await scratch.Users.SingleAsync()).DisplayName);
        // What is never loaded stayed out, and the migrations that rewrite it passed on empty tables.
        Assert.Equal((0, 0), (await scratch.Tombstones.CountAsync(), await scratch.UiSettings.CountAsync()));
    }

    /// <summary>
    /// The other migration that rewrites the cars' rows: a fill-up's odometer and cost, once columns of its own, became a reading and a
    /// cost row. A backup from before it reads as its own database became by upgrading; no migration fills a loaded table from one the
    /// reader leaves out (read in the migrations' SQL: those only rename and delete inside the sync ledger, the tombstones and the
    /// notifications).
    /// </summary>
    [Theory, MemberData(nameof(Providers))]
    public async Task ABackupFromBeforeReadingsAndCosts_ReadsAsItsOwnDatabaseBecameByUpgrading(string provider)
    {
        if (Instance(provider, "oldest") is not { } services) return;
        var (vehicle, owner, fill) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await AtMigration(services, "AddVehicleSoftDelete");
        await using (var db = await Db(services))
        {
            var model = MigrationModels.ModelOf(db, MigrationModels.IdOf(db, "AddVehicleSoftDelete")!);
            await InsertAsync(db, model, "Users", new() { ["Id"] = owner, ["Email"] = "owner@example.com", ["DisplayName"] = "Owner" });
            await InsertAsync(db, model, "Vehicles", new() { ["Id"] = vehicle, ["OwnerId"] = owner, ["Name"] = "Octavia" });
            await InsertAsync(db, model, "Refuelings", new()
            {
                ["Id"] = fill, ["VehicleId"] = vehicle, ["OwnerId"] = owner, ["Date"] = new DateOnly(2025, 5, 1), ["OdometerKm"] = 12_345, ["Liters"] = 40.5m, ["TotalCost"] = 61.25m, ["IsFullTank"] = true,
            });
        }

        // The app's own start: the backup, then the upgrade (the oracle). On MySQL the app cannot start with a fill-up at all (#345: its
        // start reads their dates), so only the backup is written there.
        var backup = provider == "MySql" ? (await services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default)).Path : await StartAsync(services, "oldest");
        await using var read = await ReadAsync(backup);

        static async Task<string> FillsAsync(AppDbContext db) => string.Join(" | ", (await db.Refuelings.IgnoreQueryFilters().AsNoTracking()
                .Include(r => r.Cost).Include(r => r.OdometerReading).OrderBy(r => r.Id).ToListAsync())
            .Select(r => FormattableString.Invariant($"{r.Id} car {r.VehicleId} {r.Date:yyyy-MM-dd} {r.Odometer} {r.Volume:0.###} {r.TotalCost:0.###} {r.Currency} full {r.IsFullTank}")));
        await using var scratch = read.Read();
        var found = await FillsAsync(scratch);
        Assert.Equal($"{fill} car {vehicle} 2025-05-01 12345 40.5 61.25 EUR full True", found);
        await using var upgraded = await Db(services);
        if (provider != "MySql") Assert.Equal(await FillsAsync(upgraded), found); // #345: MySQL cannot read a date column
    }

    public static TheoryData<string> OlderMigrations()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:", o => o.MigrationsAssembly("Tankstat.Migrations.Sqlite")).Options);
        var ids = db.Database.GetMigrations().ToList();
        return [.. ids.Select(id => MigrationNames.Of(id)!)];
    }

    /// <summary>Every backup a full restore accepts is readable (T2): one from each migration of the release loads and comes up to date with the left-out tables empty.</summary>
    [Theory, MemberData(nameof(OlderMigrations))]
    public async Task ABackupOfEveryMigration_Loads_AndComesUpToDate(string migration)
    {
        var services = Instance("Sqlite", "old")!;
        await AtMigration(services, migration);
        var backup = await services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        await using var read = await ReadAsync(backup.Path);

        await using var scratch = read.Read();
        Assert.Empty(await scratch.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await scratch.Vehicles.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>A seeded instance at today's migration: the cars' rows as they are, the people by name only, and nothing of what is never loaded.</summary>
    [Fact]
    public async Task ASeededInstancesBackup_GivesItsCarsRows_ThePeopleByNameOnly_AndNoSecrets()
    {
        await using var database = new TestDatabase(new()
        {
            ["Storage:Path"] = Path.Combine(_root, "seeded", "uploads"), ["DataProtection:KeysPath"] = Path.Combine(_root, "seeded", "keys"),
            ["Backups:Path"] = Path.Combine(_root, "seeded", "backups"),
        });
        var seeded = await BackupSeed.SeedAsync(database, Path.Combine(_root, "seeded", "uploads"), Path.Combine(_root, "seeded", "uploads"), Path.Combine(_root, "seeded", "keys"));
        var backup = await database.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);

        await using var read = await ReadAsync(backup.Path);
        await using var withEmails = await ReadAsync(backup.Path, keepEmails: true);

        await using var scratch = read.Read();
        await using var live = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var car = await scratch.Vehicles.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((seeded.Vehicle, seeded.Owner, BackupSeed.Name, BackupSeed.Plate), (car.Id, car.OwnerId, car.Name, car.LicensePlate));
        var (refueling, was) = (await scratch.Refuelings.IgnoreQueryFilters().SingleAsync(), await live.Refuelings.AsNoTracking().SingleAsync());
        Assert.Equal((was.Id, was.Date, was.Volume, was.Note, was.Odometer, was.TotalCost), (refueling.Id, refueling.Date, refueling.Volume, refueling.Note, refueling.Odometer, refueling.TotalCost));
        var expense = await scratch.Expenses.IgnoreQueryFilters().Include(e => e.Cost).Include(e => e.OdometerReading).SingleAsync();
        Assert.Equal((seeded.Expense, "Tyres", 100m, 1500L), (expense.Id, expense.Title, expense.Amount, expense.Odometer));
        Assert.Equal(1, await scratch.TyreSets.IgnoreQueryFilters().CountAsync());
        Assert.Equal(await live.Images.CountAsync(), await scratch.Images.CountAsync());

        // Who the car was whose: the name. Nothing else of the account, and none of the tables that hold a person's or the instance's own.
        await using var raw = scratch.Database.GetDbConnection().CreateCommand();
        await scratch.Database.OpenConnectionAsync();
        raw.CommandText = """SELECT "DisplayName" || '|' || "Email" || '|' || COALESCE("PasswordHash", '') FROM "Users" """;
        Assert.Equal("Zz Canary||", (string)(await raw.ExecuteScalarAsync())!);
        Assert.Equal((0, 0, 0), (await scratch.UiSettings.CountAsync(), await scratch.PhotoDrafts.CountAsync(), await scratch.InstanceState.CountAsync()));
        await using var emails = withEmails.Read();
        Assert.Equal((BackupSeed.Email, "Zz Canary"), await emails.Users.Select(u => new ValueTuple<string, string>(u.Email, u.DisplayName)).SingleAsync());
        Assert.Equal("", (await scratch.Users.SingleAsync()).Email);

        // The folder holds the database and the archive in plain, and no unpacked row any more.
        Assert.Equal(["cars.db", "data.zip"], Directory.GetFileSystemEntries(Path.GetDirectoryName(read.File)!).Select(Path.GetFileName).Where(f => !f!.StartsWith("cars.db-")).Order());
    }
}
