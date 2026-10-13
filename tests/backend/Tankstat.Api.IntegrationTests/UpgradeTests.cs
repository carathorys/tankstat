using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Api.IntegrationTests;

/// <summary>
/// The app over a database of an older release: started on one, it backs it up and migrates it, its data kept and what the newer migrations
/// add worked out; and the committed backup of an older release (<c>tests/backend/contracts/backups</c>) restores into the current one and
/// comes up to date. That file never changes: every release must still restore it.
/// </summary>
public sealed class UpgradeTests : IDisposable
{
    /// <summary>The migration the fixture was made at: before the stored consumption, so bringing it up to date works that out too.</summary>
    private const string FixtureMigration = "AddExpenses";
    private const string FixtureName = "older-migration-AddExpenses.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tankstat-it-{Guid.NewGuid():N}-upgrade");
    private string Folder(string name) => Path.Combine(_root, name);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async Task<AppDbContext> Db(TestApp app) => await app.Factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    /// <summary>The app's database taken back, by the migrations' own Down steps, to the migration named <paramref name="name"/>; its rows kept.</summary>
    private static async Task Downgrade(TestApp app, string name)
    {
        await using var db = await Db(app);
        await db.GetService<IMigrator>().MigrateAsync(MigrationModels.IdOf(db, name));
    }

    private static async Task<(string? Applied, string Latest)> Migrations(TestApp app)
    {
        await using var db = await Db(app);
        return ((await db.Database.GetAppliedMigrationsAsync()).LastOrDefault(), db.Database.GetMigrations().Last());
    }

    /// <summary>A vehicle with two full fill-ups 500 km apart (30 litres the second: 6 l/100 km) and an expense.</summary>
    private static async Task<string> AddData(TestApp app)
    {
        using var client = app.NewClient();
        var id = (await client.Gql("mutation { addVehicle(input: { name: \"Golf from before\", fuelType: PETROL }) { id } }")).Data().GetProperty("addVehicle").GetProperty("id").GetString()!;
        foreach (var (date, odometer, volume) in new[] { ("2026-09-01", 10_000, 40), ("2026-09-10", 10_500, 30) })
            (await client.Gql("mutation($i: LogRefuelingInput!) { logRefueling(input: $i) { id } }",
                new { i = new { vehicleId = id, date, volume, totalCost = volume * 600, currency = "HUF", odometer, isFullTank = true } })).Data();
        (await client.Gql("mutation($i: AddExpenseInput!) { addExpense(input: $i) { id } }",
            new { i = new { vehicleId = id, date = "2026-09-05", title = "Wash", category = "Care", amount = 4000, currency = "HUF" } })).Data();
        return id;
    }

    /// <summary>What the data must read once brought up to date: the vehicle, its fill-ups with the consumption worked out, the expense.</summary>
    private static async Task AssertData(TestApp app)
    {
        using var client = app.NewClient();
        var vehicle = Assert.Single((await client.Gql("{ myVehicles { id name } }")).Data().GetProperty("myVehicles").EnumerateArray());
        Assert.Equal("Golf from before", vehicle.GetProperty("name").GetString());
        var id = vehicle.GetProperty("id").GetString();
        var logs = (await client.Gql("query($v: UUID!) { refuelings(vehicleId: $v) { odometer consumption } }", new { v = id })).Data().GetProperty("refuelings").EnumerateArray().ToList();
        Assert.Equal(2, logs.Count);
        Assert.Equal(6.0, logs.Single(l => l.GetProperty("odometer").GetDouble() == 10_500).GetProperty("consumption").GetDouble(), 3);
        var expense = Assert.Single((await client.Gql("query($v: UUID!) { expenses(vehicleId: $v) { title amount } }", new { v = id })).Data().GetProperty("expenses").EnumerateArray());
        Assert.Equal(("Wash", 4000m), (expense.GetProperty("title").GetString(), expense.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task TheAppStartedOnAnOlderDatabase_BacksItUpFirst_ThenMigratesIt_AndKeepsItsData()
    {
        using var shared = new SharedData();
        var backups = Folder("backups");
        var settings = new Dictionary<string, string?> { ["Auth:Mode"] = "None", ["Backups:Path"] = backups };
        using (var before = shared.Host(settings))
        {
            await AddData(before);
            await Downgrade(before, FixtureMigration); // the database as an older release left it
        }

        using var after = shared.Host(settings);

        var (applied, latest) = await Migrations(after);
        Assert.Equal(latest, applied);
        await AssertData(after);
        var backup = BackupFile.ReadManifest(Assert.Single(Directory.GetFiles(backups, "*.zip")));
        Assert.Equal((FixtureMigration, true), (MigrationNames.Of(backup.Migration), backup.SafetyNet)); // taken before anything was migrated
        Assert.DoesNotContain(after.Log.From("Tankstat"), e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task TheCommittedBackupOfAnOlderRelease_IsRestored_AndBroughtUpToDate()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "contracts", "backups", FixtureName);
        var manifest = BackupFile.ReadManifest(fixture);
        Assert.Equal(FixtureMigration, MigrationNames.Of(manifest.Migration));
        var backups = Folder("backups");
        Directory.CreateDirectory(backups);
        File.Copy(fixture, Path.Combine(backups, FixtureName));
        new RestoreMarker(FixtureName, Guid.Empty, DateTimeOffset.UtcNow, null).Write(backups);

        using var app = new TestApp(new()
        {
            ["Auth:Mode"] = "None",
            ["Backups:Path"] = backups,
            ["Storage:Path"] = Folder("uploads"),
            ["Storage:PhotosPath"] = Folder("photos"),
            ["DataProtection:KeysPath"] = Folder("keys"),
        }, migrated: true);

        var (applied, latest) = await Migrations(app);
        Assert.Equal(latest, applied);
        await AssertData(app);
        Assert.False(File.Exists(RestoreMarker.PathIn(backups)));
        Assert.Empty(Directory.GetFiles(backups, "restore.*"));
    }

    /// <summary>
    /// Writes the committed fixture again: run with <c>TANKSTAT_WRITE_BACKUP_FIXTURE=1</c> only when it is to be replaced on purpose (never
    /// for a new release: an older release's backup is the point). Without the variable it checks that the fixture is there.
    /// </summary>
    [Fact]
    public async Task TheFixture_IsThere_OrIsWrittenWhenAskedFor()
    {
        var source = Path.Combine(RepositoryRoot(), "tests", "backend", "contracts", "backups", FixtureName);
        if (Environment.GetEnvironmentVariable("TANKSTAT_WRITE_BACKUP_FIXTURE") != "1")
        {
            Assert.True(File.Exists(source), $"{source} is missing");
            return;
        }
        using var app = new TestApp(new()
        {
            ["Auth:Mode"] = "None",
            ["Backups:Path"] = Folder("backups"),
            ["Storage:Path"] = Folder("uploads"),
            ["Storage:PhotosPath"] = null,
            ["DataProtection:KeysPath"] = Folder("keys"),
        }, migrated: true);
        await AddData(app);
        await Downgrade(app, FixtureMigration);
        var written = await app.Factory.Services.GetRequiredService<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.Copy(written.Path, source, overwrite: true);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Tankstat.slnx"))) return dir.FullName;
        throw new InvalidOperationException("The repository root was not found.");
    }
}
