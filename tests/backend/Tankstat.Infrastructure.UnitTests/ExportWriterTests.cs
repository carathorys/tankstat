using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Exports;
using Tankstat.Application.Imports;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Tyres;
using Tankstat.Infrastructure.Backups;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>Export my data (#341): the file a person's cars are written to, read back by the reader of cars and the import (#336 to #339).</summary>
public sealed partial class BackupImportRunTests
{
    private async Task<string> ExportAsync(TestDatabase source, Guid? exporter, IReadOnlyList<ExportCar> cars, BackupEncryption? encryption = null)
    {
        var output = Path.Combine(_root, "exports", $"{Guid.NewGuid():N}.zip");
        await source.Get<IExportWriter>().WriteAsync(new ExportWriteRequest(exporter, cars, encryption, output), _ => { }, default);
        return output;
    }

    private static BackupEncryption KeyOf(TestDatabase database, string passphrase)
    {
        var keys = database.Get<IBackupKeyMaker>().FromPassphrase(passphrase);
        return new BackupEncryption(BackupEncryption.AgeX25519, keys.Recipient, keys.Salt, Argon2idParameters.Default);
    }

    /// <summary>A plain export's tables, each a list of rows, and the names of its files.</summary>
    private static (Dictionary<string, List<JsonElement>> Tables, List<string> Files) Read(string export)
    {
        using var outer = ZipFile.OpenRead(export);
        using var data = outer.GetEntry(BackupArchive.Plain)!.Open();
        using var copy = new MemoryStream();
        data.CopyTo(copy);
        copy.Position = 0;
        using var inner = new ZipArchive(copy, ZipArchiveMode.Read);
        var tables = new Dictionary<string, List<JsonElement>>();
        foreach (var entry in inner.Entries.Where(e => e.FullName.StartsWith("tables/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            tables[entry.FullName["tables/".Length..^".jsonl".Length]] = [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.Clone())];
        }
        return (tables, [.. inner.Entries.Where(e => e.FullName.StartsWith("files/", StringComparison.Ordinal)).Select(e => e.FullName)]);
    }

    private async Task<string> OpenEncryptedAsync(TestDatabase database, Principal who, string file, string passphrase)
    {
        var sessions = database.Get<IBackupImportSessions>();
        await using var content = File.OpenRead(file);
        var status = await sessions.UploadAsync(who.Id, content, default);
        Assert.Equal(BackupImportState.NeedsKey, status.State);
        status = sessions.SubmitKey(who.Id, status.Token, passphrase, null);
        for (var i = 0; i < 600 && sessions.Find(who.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        Assert.Equal(BackupImportState.Opened, sessions.Find(who.Id, status.Token)!.State);
        return status.Token;
    }

    public static TheoryData<string, bool> ExportsByProvider => new() { { "Sqlite", false }, { "Sqlite", true }, { "PostgreSql", true }, { "SqlServer", false } };

    [Theory, MemberData(nameof(ExportsByProvider))]
    public async Task ACarsExport_ImportedOnAnotherInstance_PlainOrEncrypted_AnswersAsTheSourceCar_TyresAndCostsAlike(string provider, bool encrypted)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await TyreSourceAsync(wholeSetLines: true);
        var bob = await PersonAsync(here, "Bob");
        var export = await ExportAsync(source.Database, source.Owner.Id, [new ExportCar(source.Car, ExportKinds.All)], encrypted ? KeyOf(source.Database, "a long enough passphrase") : null);

        var token = encrypted ? await OpenEncryptedAsync(here, bob, export, "a long enough passphrase") : await OpenAsync(here, bob, export);
        Assert.True(here.Get<IBackupImportSessions>().Find(bob.Id, token)!.Summary!.IsExport);
        var report = await ReportAsync(here, (await StartAsync(here, bob, token, new ImportCarChoice(source.Car, null, Kinds: new(Tyres: new(null), Media: new())))).Id);
        Assert.Empty(report.Refused);
        var car = report.VehicleId!.Value;

        var (theirs, ours) = (await AsAsync(source.Database, source.Owner, sp => sp.GetRequiredService<TyreService>().OverviewAsync(source.Car, default)),
            await AsAsync(here, bob, sp => sp.GetRequiredService<TyreService>().OverviewAsync(car, default)));
        Assert.Equal(Picture(theirs!), Picture(ours!));
        Assert.Equal(await AsAsync(source.Database, source.Owner, sp => CostsAsync(sp, source.Car)), await AsAsync(here, bob, sp => CostsAsync(sp, car)));
        Assert.Equal(4, report.Refuelings);
    }

    [Fact]
    public async Task AnExport_HoldsTheSharedWidgetsAndTheExportersOwn_ThePhotosAndPicture_TheExporterReduced_AndNothingOfTheNeverList()
    {
        var source = await MediaSourceAsync();
        var export = await ExportAsync(source.Database, source.Anna.Id, [new ExportCar(source.Car, ExportKinds.All)]);
        var (tables, files) = Read(export);

        // Every table of the model has its file; the migrations history is whole.
        await using (var db = await source.Database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal(TableDump.Tables(db.Model).Select(t => t.Name).Order(), tables.Keys.Order());
            Assert.Equal((await db.Database.GetAppliedMigrationsAsync()).Count(), tables[TableDump.MigrationsHistory].Count);
        }
        Assert.Equal(["Anna's fuel", "Spend"], tables["VehicleCharts"].Select(c => c.GetProperty("Title").GetString()).Order()); // not Carl's own
        Assert.Equal(source.Anna.Id, Assert.Single(tables["UserDashboardLayouts"].Select(l => l.GetProperty("UserId").GetGuid()).Concat(tables["UserDashboardLayouts"].Count == 0 ? [source.Anna.Id] : [])));
        var me = Assert.Single(tables["Users"]);
        Assert.Equal((source.Anna.Id, "Anna", ""), (me.GetProperty("Id").GetGuid(), me.GetProperty("DisplayName").GetString(), me.GetProperty("Email").GetString()));
        Assert.DoesNotContain("PasswordHash", me.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Length > 0).Select(p => p.Name));
        Assert.Equal(3, tables["LogPhotos"].Count);
        Assert.Equal(4, tables["Images"].Count);
        Assert.Equal(3, files.Count(f => f.StartsWith("files/photos/", StringComparison.Ordinal)));
        Assert.Single(files, f => f.StartsWith("files/pictures/", StringComparison.Ordinal));
        // What never goes is empty, and only the tables the writer says it writes hold rows.
        Assert.All(Tankstat.Infrastructure.Exports.ExportWriter.Never, never => Assert.Empty(tables[never]));
        Assert.All(tables.Where(t => t.Value.Count > 0), t => Assert.Contains(t.Key, Tankstat.Infrastructure.Exports.ExportWriter.Written));

        // Anna takes it back into a new car of hers: the photos, the picture, the shared chart and her own come with it.
        var token = await OpenAsync(source.Database, source.Anna, export);
        var report = await ReportAsync(source.Database, (await StartAsync(source.Database, source.Anna, token,
            new ImportCarChoice(source.Car, null, Kinds: new(Media: new(Photos: true, Picture: true, Charts: true, Dashboard: true, OwnDashboard: true))))).Id);
        var (photos, _, charts, _, picture) = await MediaOfAsync(source.Database, report.VehicleId!.Value);
        Assert.Equal((3, 2, true), (photos, charts, picture is not null));
    }

    [Fact]
    public async Task KindsLeftOut_LeaveNothingThatNamesThem_AndTheTrashIsNeverWritten()
    {
        var tyres = await TyreSourceAsync(wholeSetLines: true);
        // Without the expenses: the tyre log's lines come without their amounts; a time on the car in the trash, and its places, not at all.
        var (noExpenses, _) = Read(await ExportAsync(tyres.Database, tyres.Owner.Id, [new ExportCar(tyres.Car, ExportKinds.All with { Expenses = false })]));
        Assert.Empty(noExpenses["Expenses"]);
        Assert.NotEmpty(noExpenses["TyreEntries"]);
        Assert.All(noExpenses["TyreEntries"], e => Assert.Equal(JsonValueKind.Null, e.GetProperty("ExpenseId").ValueKind));
        await using (var db = await tyres.Database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var trashed = await db.TyreInstallations.IgnoreQueryFilters().Where(i => i.VehicleId == tyres.Car && i.DeletedAt != null).Select(i => i.Id).ToListAsync();
            Assert.NotEmpty(trashed);
            Assert.DoesNotContain(noExpenses["TyreInstallations"], i => trashed.Contains(i.GetProperty("Id").GetGuid()));
            Assert.DoesNotContain(noExpenses["TyrePlaces"], p => trashed.Contains(p.GetProperty("InstallationId").GetGuid()));
            // Every reading and cost the file holds is one a written row names.
            var named = noExpenses["Refuelings"].Select(r => r.GetProperty("OdometerReadingId")).Concat(noExpenses["TyreEntries"].Select(e => e.GetProperty("OdometerReadingId")))
                .Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetGuid()).ToHashSet();
            Assert.All(noExpenses["OdometerReadings"], r => Assert.Contains(r.GetProperty("Id").GetGuid(), named));
        }

        var media = await MediaSourceAsync();
        // A refuelling in the trash is never written, nor its photos.
        var trashedFill = await AsAsync(media.Database, media.Anna, async sp =>
        {
            var fills = sp.GetRequiredService<RefuelingService>();
            var fill = (await fills.LogAsync(media.Car, new(Day.AddDays(20), 30, 45, "EUR", 11_000, true, "trashed"), default)).Id;
            await fills.DeleteAsync(fill, default);
            return fill;
        });
        // The Export tab counts what the file would take: the trashed refuelling and Carl's private chart are not counted.
        var offer = Assert.Single(await media.Database.Get<IExportFacts>().OwnCarsAsync(media.Anna.Id, default));
        Assert.Equal((1, 2, 3), (offer.Refuelings, offer.Charts, offer.Photos));
        var (bare, files) = Read(await ExportAsync(media.Database, media.Anna.Id, [new ExportCar(media.Car, ExportKinds.All with { Photos = false, Picture = false, Charts = false })]));
        Assert.DoesNotContain(bare["Refuelings"], r => r.GetProperty("Id").GetGuid() == trashedFill);
        Assert.Equal(JsonValueKind.Null, Assert.Single(bare["Vehicles"]).GetProperty("PictureImageId").ValueKind);
        Assert.Empty(bare["LogPhotos"]);
        Assert.Empty(bare["Images"]);
        Assert.Empty(files);
        Assert.Empty(bare["VehicleCharts"]);
        Assert.Empty(bare["VehicleDashboardLayouts"]);
        Assert.Empty(bare["UserDashboardLayouts"]);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task TheWriter_ReadsTheCarsFromEveryDatabase_InOneReadThatTakesNoLockToWrite(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var anna = await PersonAsync(here, "Anna");
        var (golf, other) = await AsAsync(here, anna, async sp =>
        {
            var vehicles = sp.GetRequiredService<Tankstat.Application.Vehicles.VehicleService>();
            var fills = sp.GetRequiredService<RefuelingService>();
            var golf = (await vehicles.AddAsync("Golf", null, Tankstat.Domain.Vehicles.FuelType.Petrol, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default)).Id;
            await fills.LogAsync(golf, new(Day, 40, 60, "EUR", 10_000, true, null), default);
            await fills.LogAsync(golf, new(Day.AddDays(10), 38, 57, "EUR", 10_600, true, null), default);
            var other = (await vehicles.AddAsync("Other", null, Tankstat.Domain.Vehicles.FuelType.Petrol, Tankstat.Domain.Measurements.MeasurementUnits.Metric, default)).Id;
            await fills.LogAsync(other, new(Day, 40, 60, "EUR", 5_000, true, null), default);
            return (golf, other);
        });

        var (tables, _) = Read(await ExportAsync(here, anna.Id, [new ExportCar(golf, ExportKinds.All)]));
        Assert.Equal(golf, Assert.Single(tables["Vehicles"]).GetProperty("Id").GetGuid());
        Assert.Equal(2, tables["Refuelings"].Count); // the other car's stay behind
        Assert.Equal(2, tables["Costs"].Count);
        Assert.Equal(2, tables["OdometerReadings"].Count);
        // Nothing waits on it afterwards: a save goes through at once.
        await AsAsync(here, anna, async sp => (await sp.GetRequiredService<RefuelingService>().LogAsync(other, new(Day.AddDays(5), 10, 15, "EUR", 5_100, false, null), default)).Id);
    }

    [Fact]
    public async Task EveryTableOfTheModel_HasADecision_ItGoesOrItNeverDoes()
    {
        await using var database = new TestDatabase();
        await using var db = await database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var undecided = TableDump.Tables(db.Model).Select(t => t.Name).Where(t => !Tankstat.Infrastructure.Exports.ExportWriter.Written.Contains(t) && !Tankstat.Infrastructure.Exports.ExportWriter.Never.Contains(t));
        Assert.Empty(undecided);
        Assert.Empty(Tankstat.Infrastructure.Exports.ExportWriter.Written.Intersect(Tankstat.Infrastructure.Exports.ExportWriter.Never));
    }
}
