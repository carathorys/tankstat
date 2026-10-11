using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Application.Vehicles;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;
using Tankstat.TestSupport;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>
/// The tables of a backup loaded into another database replace every row and come out of the next backup line for line (every column's
/// typing round-trips), in foreign-key order, with the instance's own new generation stamped last.
/// </summary>
public sealed class TableLoadTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), $"tankstat-load-{Guid.NewGuid():N}");
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
        foreach (var folder in _folders.Where(Directory.Exists)) Directory.Delete(folder, recursive: true);
    }

    private TestDatabase Database(string name) => new(new Dictionary<string, string?>
    {
        ["Storage:Path"] = Path.Combine(_data, name, "uploads"),
        ["DataProtection:KeysPath"] = Path.Combine(_data, name, "keys"),
        ["Backups:Path"] = Path.Combine(_data, name, "backups"),
    });

    private string Inner(string file)
    {
        var folder = BackupSeed.Inner(file);
        _folders.Add(folder);
        return folder;
    }

    private static Task<AppDbContext> Context(TestDatabase db) => db.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    [Fact]
    public async Task LoadOrder_PutsEveryTableAfterTheOnesItRefersTo()
    {
        await using var db = Database("order");
        await using var context = await Context(db);

        var order = TableLoad.InLoadOrder(TableDump.Tables(context.Model)).Select(t => t.Name).ToList();

        Assert.DoesNotContain(TableDump.MigrationsHistory, order);
        Assert.True(order.IndexOf("Vehicles") < order.IndexOf("Refuelings"));
        Assert.True(order.IndexOf("OdometerReadings") < order.IndexOf("Refuelings"));
        Assert.True(order.IndexOf("Costs") < order.IndexOf("Expenses"));
        Assert.True(order.IndexOf("RecurringExpenses") < order.IndexOf("RecurringCompletions"));
        Assert.True(order.IndexOf("Users") < order.IndexOf("UserSessions"));
        Assert.True(order.IndexOf("Images") < order.IndexOf("PhotoReadings"));
        Assert.Equal(context.Model.GetEntityTypes().Select(e => e.GetTableName()).Distinct().Count(), order.Count);
    }

    [Fact]
    public async Task ABackupLoadedIntoAnotherDatabase_ComesOutOfItsBackupLineForLine_AndGetsItsOwnGeneration()
    {
        await using var a = Database("a");
        var seeded = await BackupSeed.SeedAsync(a, Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "keys"));
        await using (var context = await Context(a))
        {
            context.Instance.Add(Domain.Backups.Instance.Fresh());
            await context.SaveChangesAsync();
        }
        var backupA = await a.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var innerA = Inner(backupA.Path);

        await using var b = Database("b");
        var other = await BackupSeed.SeedAsync(b, Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "keys")); // rows of its own, all replaced
        var restoredAt = new DateTimeOffset(2026, 10, 12, 3, 0, 0, TimeSpan.Zero);
        InstanceInfo instance;
        await using (var context = await Context(b))
            instance = await TableLoad.LoadAsync(context, Path.Combine(innerA, "tables"), backupA.Manifest.Contents.Tables, restoredAt, backupA.Manifest.CreatedAt, b.Log.For<TableLoadTests>(), default);

        var backupB = await b.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var innerB = Inner(backupB.Path);
        foreach (var table in backupA.Manifest.Contents.Tables.Keys.Where(t => t != "Instance"))
            Assert.Equal(BackupSeed.Lines(innerA, table).Order(), BackupSeed.Lines(innerB, table).Order());
        Assert.Equal(backupA.Manifest.Contents.Tables.Where(t => t.Key != "Instance"), backupB.Manifest.Contents.Tables.Where(t => t.Key != "Instance"));

        // The generation is B's own, not A's; the rows are A's, readable by the app (typed as EF expects them).
        var instanceA = BackupSeed.Lines(innerA, "Instance").Single();
        var instanceB = BackupSeed.Lines(innerB, "Instance").Single();
        Assert.NotEqual(instanceA, instanceB);
        Assert.Contains(instance.Generation.ToString("D"), instanceB);
        Assert.Equal(backupA.Manifest.CreatedAt, instance.RestorePoint);
        var vehicle = await b.Get<IVehicleRepository>().FindAsync(seeded.Vehicle, default);
        Assert.Equal(BackupSeed.Name, vehicle!.Name);
        Assert.Null(await b.Get<IVehicleRepository>().FindAsync(other.Vehicle, default));
        await using (var context = await Context(b))
        {
            var refueling = await context.Refuelings.Include(r => r.OdometerReading).Include(r => r.Cost).SingleAsync(r => r.Id == seeded.Refueling);
            Assert.Equal((BackupSeed.Day, 40m, 60m, 1000L, true), (refueling.Date, refueling.Volume, refueling.Cost!.Amount, refueling.OdometerReading!.Value, refueling.IsFullTank));
            Assert.Equal(1, await context.Instance.CountAsync());
            Assert.Equal(restoredAt, (await context.Instance.SingleAsync()).RestoredAt);
        }
    }

    [Fact]
    public async Task ARowCountThatDiffersFromTheManifest_FailsTheLoad_AndLeavesTheDatabaseAsItWas()
    {
        await using var a = Database("a");
        await BackupSeed.SeedAsync(a, Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "keys"));
        var backup = await a.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var inner = Inner(backup.Path);
        File.AppendAllText(Path.Combine(inner, "tables", "Vehicles.jsonl"), BackupSeed.Lines(inner, "Vehicles")[0].Replace(BackupSeed.Name, "Another") + "\n"); // a row the manifest does not count (and a duplicate key)

        await using var b = Database("b");
        var own = await BackupSeed.SeedAsync(b, Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "keys"));
        await using var context = await Context(b);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            TableLoad.LoadAsync(context, Path.Combine(inner, "tables"), backup.Manifest.Contents.Tables, DateTimeOffset.UtcNow, backup.Manifest.CreatedAt, b.Log.For<TableLoadTests>(), default));

        Assert.NotNull(await b.Get<IVehicleRepository>().FindAsync(own.Vehicle, default)); // rolled back
    }
}
