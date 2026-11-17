using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Images;
using Tankstat.Application.Imports;
using Tankstat.Application.Photos;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Stats;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>A car's photos, picture, widgets and dashboards from a backup (#339), through the services.</summary>
public sealed partial class BackupImportRunTests
{
    /// <summary>A file the app takes for a PNG (it checks the signature, never re-encodes): its last bytes make it a file of its own.</summary>
    private static byte[] Png(byte tag) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, tag, tag, tag];

    private static ChartConfig Recipe(ChartMetric metric = ChartMetric.TotalSpend) => new(metric, ChartGrouping.Month, ChartKind.Bar, ChartRange.Last6Months, false);

    private sealed record MediaSource(TestDatabase Database, Principal Anna, string Backup, Guid Car, Guid Shared, Guid Own);

    /// <summary>
    /// Anna's Clio: a refuelling with two photos and an expense with one, a picture, a shared chart, a chart of her own and one of Carl's
    /// own, the vehicle's dashboard with the shared chart's tile and Carl's, and her own dashboard with her chart's tile.
    /// </summary>
    private async Task<MediaSource> MediaSourceAsync()
    {
        var source = Instance("Sqlite", "media")!;
        var (anna, carl) = (await PersonAsync(source, "Anna"), await PersonAsync(source, "Carl"));
        var (car, shared, own) = await AsAsync(source, anna, async sp =>
        {
            var car = (await sp.GetRequiredService<VehicleService>().AddAsync("Clio", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fill = (await sp.GetRequiredService<RefuelingService>().LogAsync(car, new(Day, 40, 60, "EUR", 10_000, true, "fill"), default)).Id;
            var wash = (await sp.GetRequiredService<ExpenseService>().AddAsync(car, new(Day.AddDays(1), "Wash", null, 12, "EUR", null, null), default)).Id;
            var photos = sp.GetRequiredService<LogPhotoService>();
            await photos.AddAsync(LogType.Refueling, fill, Png(1), default);
            await photos.AddAsync(LogType.Refueling, fill, Png(2), default);
            await photos.AddAsync(LogType.Expense, wash, Png(3), default);
            await sp.GetRequiredService<ImageService>().SetVehiclePictureAsync(car, Png(9), default);
            var charts = sp.GetRequiredService<ChartService>();
            var shared = (await charts.CreateAsync(car, new ChartInput("Spend", Recipe(), true), default)).Id;
            var own = (await charts.CreateAsync(car, new ChartInput("Anna's fuel", Recipe(ChartMetric.FuelVolume), false), default)).Id;
            await sp.GetRequiredService<Tankstat.Application.Sharing.ResourceSharingService>().SetLogAccessAsync(car, carl.Id, Tankstat.Domain.Access.AccessLevel.Edit, default);
            return (car, shared, own);
        });
        await AsAsync(source, carl, async sp => (await sp.GetRequiredService<ChartService>().CreateAsync(car, new ChartInput("Carl's", Recipe(ChartMetric.FuelCost), false), default)).Id);
        await AsAsync(source, anna, async sp =>
        {
            var dashboards = sp.GetRequiredService<DashboardService>();
            await dashboards.SaveAsync(car, false, new DashboardLayoutValues([new DashboardTile($"chart:{shared:D}", 0, 0, 2, 2)], []), default);
            await dashboards.SaveAsync(car, true, new DashboardLayoutValues([new DashboardTile($"chart:{own:D}", 0, 0, 2, 2), new DashboardTile($"chart:{shared:D}", 2, 0, 2, 2)], []), default);
            return 0;
        });
        var backup = await source.Get<IBackupWriter>().WriteAsync(new BackupRequest(Photos: true), default);
        return new MediaSource(source, anna, backup.Path, car, shared, own);
    }

    private static async Task<(int Photos, int Images, int Charts, int Tiles, Guid? Picture)> MediaOfAsync(TestDatabase here, Guid car)
    {
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var layout = await db.VehicleDashboardLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.VehicleId == car);
        return (await db.LogPhotos.CountAsync(p => p.VehicleId == car), await db.Images.CountAsync(), await db.VehicleCharts.CountAsync(c => c.VehicleId == car),
            layout?.Values.Tiles.Count ?? 0, (await db.Vehicles.SingleAsync(v => v.Id == car)).PictureImageId);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarsPhotosPictureChartsAndDashboard_Come_TheirTilesPointedAtTheNewCharts_AndASecondImportAddsNoPhoto(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await MediaSourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var all = new ImportMediaChoice(Photos: true, Picture: true, Charts: true, Dashboard: true);
        var token = await OpenAsync(here, bob, source.Backup);

        var review = Assert.Single(await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(source.Car, null, Kinds: new(Media: all))], default))).Media!;
        Assert.Equal((3, 0, false, true, false, 1, 0, 0, true, false, false), (review.PhotosNew, review.PhotosHere, review.PhotosLeftOut, review.Picture, review.TargetPicture,
            review.ChartsNew, review.ChartsHere, review.OwnChartsNew, review.Dashboard, review.TargetDashboard, review.OwnDashboard)); // Bob: only the shared chart, no own dashboard
        var report = await ReportAsync(here, (await StartAsync(here, bob, token, new ImportCarChoice(source.Car, null, Kinds: new(Media: all)))).Id);

        Assert.Equal(new ImportMediaReport(3, 0, true, 1, 0, true, false, 0), report.Media);
        var car = report.VehicleId!.Value;
        var (photos, images, charts, tiles, picture) = await MediaOfAsync(here, car);
        Assert.Equal((3, 4, 1, 1), (photos, images, charts, tiles));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var chart = await db.VehicleCharts.SingleAsync(c => c.VehicleId == car);
            Assert.Equal(("Spend", bob.Id, true), (chart.Title, chart.CreatedById, chart.IsShared)); // the importer the creator, a new id
            Assert.Equal($"chart:{chart.Id:D}", (await db.VehicleDashboardLayouts.SingleAsync(l => l.VehicleId == car)).Values.Tiles.Single().Id);
            Assert.Equal(0, await db.PhotoReadings.CountAsync()); // no reading queued
            // The refuelling's two photos in the backup's order.
            var fill = await db.Refuelings.SingleAsync(r => r.VehicleId == car);
            var order = await db.LogPhotos.Where(p => p.LogId == fill.Id).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Select(p => p.ImageId).ToListAsync();
            var tags = new List<byte>();
            foreach (var id in order)
            {
                var image = await db.Images.SingleAsync(i => i.Id == id);
                await using var stream = (await here.Get<IImageStore>().OpenReadAsync(image, default))!;
                tags.Add((await ReadAllAsync(stream))[^1]);
            }
            Assert.Equal([(byte)1, (byte)2], tags);
        }

        // Again into that car: every photo is there by its file, the chart by its title and recipe; nothing is added.
        var again = await ReportAsync(here, (await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup),
            new ImportCarChoice(source.Car, car, Kinds: new(Media: all with { Picture = false, Dashboard = false })))).Id);
        Assert.Equal(new ImportMediaReport(0, 3, false, 0, 1, false, false, 0), again.Media);
        Assert.Equal((3, 4, 1), (await MediaOfAsync(here, car)) is var m ? (m.Photos, m.Images, m.Charts) : default);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task APictureReplacedInACarPutBack_LeavesTheOldPictureAndItsFile_AndTheNewFileGoes(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await MediaSourceAsync();
        var bob = await PersonAsync(here, "Bob");
        // Bob's Clio has a picture of its own and his thirty widgets already: the backup's chart is refused, and he leaves the car out then.
        var (own, old) = await AsAsync(here, bob, async sp =>
        {
            var own = (await sp.GetRequiredService<VehicleService>().AddAsync("My Clio", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var old = await sp.GetRequiredService<ImageService>().SetVehiclePictureAsync(own, Png(7), default);
            for (var i = 0; i < ChartService.MaxPerUserAndVehicle; i++)
                await sp.GetRequiredService<ChartService>().CreateAsync(own, new ChartInput($"Mine {i}", Recipe(), false), default);
            return (own, old);
        });
        var choice = new ImportCarChoice(source.Car, own, RefusedEntryPolicy.LeaveCarOut, Kinds: new(Media: new(Photos: false, Picture: true, Charts: true)));
        var run = await FinishedAsync(here, (await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), choice)).Id);
        var report = ImportRunJson.Read<ImportRunReport>(run.Report)!.Cars.Single();

        // The run committed (the car's savepoint was rolled back, the run went on): the old picture stays, its file on disk, the new file gone.
        Assert.True(report.LeftOut);
        var refused = Assert.Single(report.Refused);
        Assert.Equal((BackupMediaWork.ChartKind, "chart.limit"), (refused.Kind, refused.Key));
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(old, (await db.Vehicles.SingleAsync(v => v.Id == own)).PictureImageId);
        var image = await db.Images.SingleAsync(i => i.Id == old);
        await using (var stream = await here.Get<IImageStore>().OpenReadAsync(image, default)) Assert.NotNull(stream);
        Assert.Equal(1, await db.Images.CountAsync(i => i.Folder == image.Folder));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "here", "uploads", image.Folder!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task AnExportersOwnChartsAndDashboard_ComeBackToThem_AndAReplaceAllCountsThePhotosThatDoNot()
    {
        var source = await MediaSourceAsync();
        // Anna imports her own backup into her instance: her own chart and dashboard come back to her (the owner's answer on #339), Carl's own not.
        var all = new ImportMediaChoice(Photos: true, Picture: false, Charts: true, Dashboard: true);
        var token = await OpenAsync(source.Database, source.Anna, source.Backup);
        var report = await ReportAsync(source.Database, (await StartAsync(source.Database, source.Anna, token, new ImportCarChoice(source.Car, null, Kinds: new(Media: all)))).Id);
        Assert.Equal((2, true, true, 0), (report.Media!.Charts, report.Media.Dashboard, report.Media.OwnDashboard, report.Media.TilesLeftOut));
        await using (var db = await source.Database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var mine = await db.VehicleCharts.Where(c => c.VehicleId == report.VehicleId).OrderBy(c => c.Title).Select(c => new { c.Title, c.IsShared }).ToListAsync();
            Assert.Equal([("Anna's fuel", false), ("Spend", true)], mine.Select(c => (c.Title, c.IsShared)));
            var personal = await db.UserDashboardLayouts.SingleAsync(l => l.VehicleId == report.VehicleId && l.UserId == source.Anna.Id);
            Assert.Equal(2, personal.Values.Tiles.Count);
        }

        // A Replace all of refuellings into the imported car with a photo of its own that the backup does not have: counted, then gone.
        var car = report.VehicleId!.Value;
        await AsAsync(source.Database, source.Anna, async sp =>
        {
            var fill = (await sp.GetRequiredService<RefuelingService>().ListAsync(car, new RefuelingQuery(), default)).Single().Id;
            return await sp.GetRequiredService<LogPhotoService>().AddAsync(LogType.Refueling, fill, Png(5), default);
        });
        var replace = new ImportCarChoice(source.Car, car, Kinds: new(Refuelings: new(ImportKindMode.ReplaceAll), Media: new(Photos: true, Charts: false)), ConfirmedName: "Clio");
        var again = await OpenAsync(source.Database, source.Anna, source.Backup);
        var lost = Assert.Single(await AsAsync(source.Database, source.Anna, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(again, [replace], default))).Kinds[0];
        Assert.Equal((ImportKindMode.ReplaceAll, 1), (lost.Mode, lost.PhotosLost)); // the two the backup has come back; the fifth does not
        var replaced = await ReportAsync(source.Database, (await StartAsync(source.Database, source.Anna, again, replace)).Id);
        Assert.Equal((1, 2), (replaced.Kinds![0].Deleted, replaced.Media!.Photos));
        await using var after = await source.Database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var fills = await after.Refuelings.Where(r => r.VehicleId == car).Select(r => r.Id).ToListAsync();
        Assert.Equal(2, await after.LogPhotos.CountAsync(p => fills.Contains(p.LogId)));
    }
}
