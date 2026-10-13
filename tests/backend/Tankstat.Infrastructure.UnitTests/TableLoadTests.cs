using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
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
            context.InstanceState.Add(Domain.Instance.InstanceState.New());
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
        foreach (var table in backupA.Manifest.Contents.Tables.Keys.Where(t => t != "InstanceState"))
            Assert.Equal(BackupSeed.Lines(innerA, table).Order(), BackupSeed.Lines(innerB, table).Order());
        Assert.Equal(backupA.Manifest.Contents.Tables.Where(t => t.Key != "InstanceState"), backupB.Manifest.Contents.Tables.Where(t => t.Key != "InstanceState"));

        // The generation is B's own, not A's; the rows are A's, readable by the app (typed as EF expects them).
        var instanceA = BackupSeed.Lines(innerA, "InstanceState").Single();
        var instanceB = BackupSeed.Lines(innerB, "InstanceState").Single();
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
            Assert.Equal(1, await context.InstanceState.CountAsync());
            Assert.Equal(restoredAt, (await context.InstanceState.SingleAsync()).RestoredAt);
        }
    }

    [Fact]
    public async Task ARowCountThatDiffersFromTheManifest_FailsTheLoad_AndLeavesTheDatabaseAsItWas()
    {
        await using var a = Database("a");
        await BackupSeed.SeedAsync(a, Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "keys"));
        var backup = await a.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var inner = Inner(backup.Path);
        // A row the manifest does not count, under an id of its own: it loads, so only the count can tell.
        var first = BackupSeed.Lines(inner, "Vehicles")[0];
        var id = JsonDocument.Parse(first).RootElement.GetProperty("Id").GetString()!;
        File.AppendAllText(Path.Combine(inner, "tables", "Vehicles.jsonl"), first.Replace(id, Guid.NewGuid().ToString("D")).Replace(BackupSeed.Name, "Another") + "\n");

        await using var b = Database("b");
        var own = await BackupSeed.SeedAsync(b, Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "keys"));
        await using var context = await Context(b);
        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            TableLoad.LoadAsync(context, Path.Combine(inner, "tables"), backup.Manifest.Contents.Tables, DateTimeOffset.UtcNow, backup.Manifest.CreatedAt, b.Log.For<TableLoadTests>(), default));

        Assert.Equal("backup.corrupt", refused.Key);
        Assert.NotNull(await b.Get<IVehicleRepository>().FindAsync(own.Vehicle, default)); // rolled back
    }

    [Fact]
    public async Task ATableWhoseFileIsMissing_IsEmptyWhenTheManifestSaysNoRows_AndFailsTheLoadWhenItSaysRows()
    {
        await using var a = Database("a");
        await BackupSeed.SeedAsync(a, Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "keys"));
        var backup = await a.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var inner = Inner(backup.Path);
        Assert.Equal(0, backup.Manifest.Contents.Tables["InstanceState"]); // a bare test database has no generation row
        File.Delete(Path.Combine(inner, "tables", "InstanceState.jsonl"));
        await using var b = Database("b");
        var own = await BackupSeed.SeedAsync(b, Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "uploads"), Path.Combine(_data, "b", "keys"));

        await using (var context = await Context(b))
            await TableLoad.LoadAsync(context, Path.Combine(inner, "tables"), backup.Manifest.Contents.Tables, DateTimeOffset.UtcNow, backup.Manifest.CreatedAt, b.Log.For<TableLoadTests>(), default); // fine: it had none

        // The vehicles file went missing from the backup: loading on would empty a table that had a row.
        File.Delete(Path.Combine(inner, "tables", "Vehicles.jsonl"));
        await using var c = Database("c");
        var other = await BackupSeed.SeedAsync(c, Path.Combine(_data, "c", "uploads"), Path.Combine(_data, "c", "uploads"), Path.Combine(_data, "c", "keys"));
        await using (var context = await Context(c))
        {
            var refused = await Assert.ThrowsAsync<DomainException>(() =>
                TableLoad.LoadAsync(context, Path.Combine(inner, "tables"), backup.Manifest.Contents.Tables, DateTimeOffset.UtcNow, backup.Manifest.CreatedAt, c.Log.For<TableLoadTests>(), default));
            Assert.Equal("backup.corrupt", refused.Key);
            Assert.Equal(("Vehicles", 1L, 0L), (refused.Args["table"], refused.Args["expected"], refused.Args["actual"]));
        }
        Assert.NotNull(await c.Get<IVehicleRepository>().FindAsync(other.Vehicle, default)); // rolled back: nothing was emptied
        Assert.Null(await b.Get<IVehicleRepository>().FindAsync(own.Vehicle, default)); // b was loaded the first time: its own vehicle is gone
    }

    [Fact]
    public async Task ATableTheModelDoesNotKnow_IsWarnedAboutAndLeftOut()
    {
        await using var a = Database("a");
        await BackupSeed.SeedAsync(a, Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "uploads"), Path.Combine(_data, "a", "keys"));
        var backup = await a.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var inner = Inner(backup.Path);
        var expected = new Dictionary<string, long>(backup.Manifest.Contents.Tables) { ["FromTheFuture"] = 3 };
        await using var b = Database("b");

        await using (var context = await Context(b))
            await TableLoad.LoadAsync(context, Path.Combine(inner, "tables"), expected, DateTimeOffset.UtcNow, backup.Manifest.CreatedAt, b.Log.For<TableLoadTests>(), default);

        var warning = Assert.Single(b.Log.From<TableLoadTests>().Where(e => e.Level == LogLevel.Warning));
        Assert.Equal("FromTheFuture", warning.Values["Table"]);
    }

    [Fact]
    public void TablesThatReferToEachOther_AreRefusedInsteadOfLoopingForever()
    {
        DumpTable Table(string name, params string[] dependsOn) => new(name, null, [], [], new HashSet<string>(dependsOn));

        var cycle = Assert.Throws<InvalidOperationException>(() => TableLoad.InLoadOrder([Table("A", "B"), Table("B", "C"), Table("C", "A"), Table("D")]));

        Assert.Contains("A, B, C", cycle.Message);
        Assert.Equal(["Roots", "Leaf"], TableLoad.InLoadOrder([Table("Leaf", "Roots"), Table("Roots")]).Select(t => t.Name));
    }

    private static object RoundTrip(object value, Type type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) TableDump.WriteValue(writer, value);
        using var document = JsonDocument.Parse(stream.ToArray());
        return TableLoad.Parse(document.RootElement, type);
    }

    public static TheoryData<object, Type> Values => new()
    {
        { "text with \"quotes\" and ünïcode", typeof(string) },
        { Guid.Parse("0b2f6bd4-5c1e-4a3a-8f1e-3c2d1a0b9e8f"), typeof(Guid) },
        { true, typeof(bool) },
        { -42, typeof(int) },
        { long.MaxValue, typeof(long) },
        { (short)-7, typeof(short) },
        { (byte)200, typeof(byte) },
        { 1234567.8901m, typeof(decimal) },
        { 0.1 + 0.2, typeof(double) },
        { 2.25f, typeof(float) },
        { new DateTime(2026, 10, 11, 12, 30, 45, DateTimeKind.Utc).AddTicks(1234567), typeof(DateTime) },
        { new DateOnly(2026, 2, 28), typeof(DateOnly) },
        { new TimeOnly(23, 59, 58).Add(TimeSpan.FromTicks(1234567)), typeof(TimeOnly) },
        { TimeSpan.FromTicks(-9876543210), typeof(TimeSpan) },
        { new byte[] { 0, 1, 2, 254, 255 }, typeof(byte[]) },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void EveryTypeAColumnCanHold_WritesAndParsesBackToItself(object value, Type type) => Assert.Equal(value, RoundTrip(value, type));

    [Fact]
    public void ADateTime_IsDumpedAsUtc_WhateverItsKind_AndAnOffsetBecomesTheSameInstant()
    {
        var local = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Unspecified);
        var offset = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(local.ToUniversalTime(), RoundTrip(local, typeof(DateTime)));
        Assert.Equal(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc), RoundTrip(unspecified, typeof(DateTime))); // the app stores UTC: a kind-less value is that
        Assert.Equal(DateTimeKind.Utc, ((DateTime)RoundTrip(local, typeof(DateTime))).Kind); // what Npgsql's timestamptz demands
        Assert.Equal(offset, RoundTrip(offset, typeof(DateTimeOffset)));
        Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)RoundTrip(offset, typeof(DateTimeOffset))).Offset);
    }

    [Fact]
    public void AValueThatIsNotWhatTheColumnHolds_IsACorruptBackup_AndATypeNoColumnHolds_IsRefusedBothWays()
    {
        static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

        foreach (var (text, type) in new[] { ("\"not a guid\"", typeof(Guid)), ("\"x\"", typeof(DateTime)), ("\"28/02/2026\"", typeof(DateOnly)), ("\"~\"", typeof(byte[])), ("12.5", typeof(int)), ("\"x\"", typeof(TimeSpan)) })
            Assert.Equal("backup.corrupt", Assert.Throws<DomainException>(() => TableLoad.Parse(Json(text), type)).Key);
        Assert.Throws<NotSupportedException>(() => TableLoad.Parse(Json("1"), typeof(object)));
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        Assert.Throws<NotSupportedException>(() => TableDump.WriteValue(writer, new object()));
    }
}
