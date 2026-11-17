using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Images;
using Tankstat.Application.Imports;
using Tankstat.Application.Photos;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Runs;
using Tankstat.Application.Sharing;
using Tankstat.Application.Stats;
using Tankstat.Application.Tyres;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Tyres;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The partial restore (#340): a car put back as it was, from a restore point, under its own ids.</summary>
public sealed partial class BackupImportRunTests
{
    private sealed record RestoreSource(string Backup, Guid Car, Principal Anna, Principal Bob, Principal Carol, Principal Ada);

    /// <summary>
    /// Anna's Golf on this instance: fill-ups, an expense edited twice, a schedule, a tyre history (a set with its times on the car, a fee,
    /// tracked tyre by tyre, a puncture of two tyres sharing an expense, a tyre replaced, a rotation; a set with a price), photos Bob added,
    /// a picture, a shared chart, Bob's and Carol's own charts, the vehicle's dashboard and Bob's own, shared with Bob and Carol. Backed up.
    /// </summary>
    private async Task<RestoreSource> RestoreSourceAsync(TestDatabase here)
    {
        var (anna, bob, carol, ada) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Bob"), await PersonAsync(here, "Carol"), await PersonAsync(here, "Ada", admin: true));
        var car = await AsAsync(here, anna, async sp =>
        {
            var car = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", "ABC-123", FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            foreach (var (day, km) in new[] { (D(2024, 10, 1), 10_000L), (D(2025, 3, 1), 15_000L), (D(2025, 11, 1), 20_000L) })
                await fills.LogAsync(car, new(day, 40, 60, "EUR", km, true, null), default);
            var expenses = sp.GetRequiredService<ExpenseService>();
            var wash = (await expenses.AddAsync(car, new ExpenseInput(D(2025, 4, 1), "Wash", "Care", 12, "EUR", null, null), default)).Id;
            await expenses.UpdateAsync(wash, new ExpenseInput(D(2025, 4, 1), "Car wash", "Care", 12, "EUR", null, null), default);
            await expenses.UpdateAsync(wash, new ExpenseInput(D(2025, 4, 1), "Car wash", "Care", 14, "EUR", null, "Polish too"), default);
            await sp.GetRequiredService<RecurringExpenseService>().AddAsync(car, new("Oil change", "Service", null, RecurrenceKind.Time, 12, null, D(2025, 1, 1), null, null, null), default);
            var tyres = sp.GetRequiredService<TyreService>();
            Guid winter = Guid.NewGuid(), summer = Guid.NewGuid(), i1 = Guid.NewGuid();
            await tyres.AddSetAsync(new NewTyreSet(car, new TyreSetInput("Nokian WR", TyreSeason.Winter, "205/55 R16", D(2022, 11, 1), 40, 2022, 12_000), false, D(2024, 11, 10), null, null), default, winter);
            await tyres.AddInstallationAsync(winter, new TyreInstallationInput(D(2024, 11, 10), D(2025, 3, 28), true, null, null), default, i1);
            await tyres.AddSetAsync(new NewTyreSet(car, new TyreSetInput("Michelin Primacy", TyreSeason.Summer, null, D(2023, 3, 1), null, null), false, D(2025, 3, 28), null, null,
                Price: new TyreFee(400, "EUR", "Tyres", "Bought: Michelin"), BoughtOn: D(2023, 3, 5)), default, summer);
            await tyres.AddInstallationAsync(summer, new TyreInstallationInput(D(2025, 3, 28), D(2025, 11, 8), false, 15_200, null), default);
            await tyres.AddInstallationAsync(winter, new TyreInstallationInput(D(2025, 11, 8), null, true, null, null), default);
            await tyres.UpdateEntryAsync(i1, new TyreEntryInput(TyreEntryKind.Swap, winter, D(2024, 11, 10), null, "Fitted at Bob's", new TyreFee(30, "EUR", "Tyres", "Swap to winter")), default);
            var w = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
            await tyres.BreakUpAsync(new TyreBreakUpInput(winter, D(2025, 1, 5),
                [new NewTyre(w[0], "W1", TyreCorner.FrontLeft), new NewTyre(w[1], "W2", TyreCorner.FrontRight), new NewTyre(w[2], "W3", TyreCorner.RearLeft), new NewTyre(w[3], "W4", TyreCorner.RearRight)]), default);
            var (l1, l2, punct) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await expenses.AddAsync(car, new ExpenseInput(D(2025, 2, 1), "Puncture", "Tyres", 20, "EUR", null, "Paid cash"), default, id: punct,
                tyre: new ExpenseTyreDetails(TyreEntryKind.PunctureRepaired, winter, [new TyreLine(w[0], l1), new TyreLine(w[1], l2)]));
            await tyres.ReplaceTyresAsync(new TyreReplaceInput(winter, D(2025, 12, 1), null, TyreRetiredWhy.Damaged, [new TyreReplacement(w[0], w[4], "W5", "Nokian new", 45, 2025)],
                new TyreFee(120, "EUR", "Tyres", "W5")), default);
            await tyres.ChangePositionsAsync(new TyreSwapInput(car, winter, D(2026, 1, 10), null, false, null, "Rotated",
                Corners: new Dictionary<Guid, TyreCorner?> { [w[4]] = TyreCorner.RearLeft, [w[1]] = TyreCorner.RearRight, [w[2]] = TyreCorner.FrontLeft, [w[3]] = TyreCorner.FrontRight }), default);
            await sp.GetRequiredService<ImageService>().SetVehiclePictureAsync(car, Png(9), default);
            var sharing = sp.GetRequiredService<ResourceSharingService>();
            await sharing.SetLogAccessAsync(car, bob.Id, AccessLevel.Edit, default);
            await sharing.SetLogAccessAsync(car, carol.Id, AccessLevel.Edit, default);
            return car;
        });
        var bobs = await AsAsync(here, bob, async sp =>
        {
            var fill = (await sp.GetRequiredService<RefuelingService>().ListAsync(car, new RefuelingQuery(Take: 10), default)).First().Id;
            await sp.GetRequiredService<LogPhotoService>().AddAsync(LogType.Refueling, fill, Png(1), default);
            await sp.GetRequiredService<LogPhotoService>().AddAsync(LogType.Refueling, fill, Png(2), default);
            return (await sp.GetRequiredService<ChartService>().CreateAsync(car, new ChartInput("Bob's", Recipe(ChartMetric.FuelCost), false), default)).Id;
        });
        await AsAsync(here, carol, async sp => (await sp.GetRequiredService<ChartService>().CreateAsync(car, new ChartInput("Carol's", Recipe(ChartMetric.FuelVolume), false), default)).Id);
        await AsAsync(here, anna, async sp =>
        {
            var shared = (await sp.GetRequiredService<ChartService>().CreateAsync(car, new ChartInput("Spend", Recipe(), true), default)).Id;
            await sp.GetRequiredService<DashboardService>().SaveAsync(car, false, new DashboardLayoutValues([new DashboardTile($"chart:{shared:D}", 0, 0, 2, 2)], []), default);
            return 0;
        });
        await AsAsync(here, bob, async sp =>
        {
            await sp.GetRequiredService<DashboardService>().SaveAsync(car, true, new DashboardLayoutValues([new DashboardTile($"chart:{bobs:D}", 0, 0, 2, 2)], []), default);
            return 0;
        });
        var backup = await here.Get<IBackupWriter>().WriteAsync(new BackupRequest(Photos: true), default);
        return new RestoreSource(backup.Path, car, anna, bob, carol, ada);
    }

    /// <summary>Every kept-id table of the car as lines: id, owner, creator, version, creation time.</summary>
    private static async Task<Dictionary<string, List<string>>> RowsOfAsync(TestDatabase here, Guid car)
    {
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        static List<string> Sorted(IEnumerable<string> lines) => [.. lines.Order(StringComparer.Ordinal)];
        static string T(DateTimeOffset? at) => at?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff") ?? "-";
        return new()
        {
            ["Vehicles"] = Sorted((await db.Vehicles.IgnoreQueryFilters().Where(v => v.Id == car).ToListAsync()).Select(v => $"{v.Id} owner {v.OwnerId} v{v.Version} pic {v.PictureImageId}")),
            ["Refuelings"] = Sorted((await db.Refuelings.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version}")),
            ["Expenses"] = Sorted((await db.Expenses.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version}")),
            ["RecurringExpenses"] = Sorted((await db.RecurringExpenses.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version} {T(r.CreatedAt)}")),
            ["TyreSets"] = Sorted((await db.TyreSets.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version}")),
            ["TyreInstallations"] = Sorted((await db.TyreInstallations.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version}")),
            ["TyreEntries"] = Sorted((await db.TyreEntries.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version}")),
            ["Tyres"] = Sorted((await db.Tyres.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version}")),
            ["TyrePlaces"] = Sorted((await db.TyrePlaces.IgnoreQueryFilters().Where(r => r.VehicleId == car).ToListAsync())
                .Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} v{r.Version} tyre {r.TyreId} on {r.InstallationId} {r.From}..{r.Until} {r.Corner} trashed {r.DeletedAt is not null}")),
            ["VehicleCharts"] = Sorted((await db.VehicleCharts.Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.CreatedById} {r.Title} {T(r.CreatedAt)}")),
            ["LogPhotos"] = Sorted((await db.LogPhotos.Where(r => r.VehicleId == car).ToListAsync()).Select(r => $"{r.Id} {r.OwnerId} {r.CreatedById} {r.ImageId} {r.LogId} {T(r.CreatedAt)}")),
            ["ResourceGrants"] = Sorted((await db.ResourceGrants.Where(g => g.ResourceId == car).ToListAsync()).Select(g => $"{g.GranteeId} {g.Level}")),
            ["Dashboards"] = Sorted([.. (await db.VehicleDashboardLayouts.Where(l => l.VehicleId == car).ToListAsync()).Select(l => $"shared {string.Join(",", l.Values.Tiles.Select(t => t.Id))}"),
                .. (await db.UserDashboardLayouts.Where(l => l.VehicleId == car).ToListAsync()).Select(l => $"{l.UserId} {string.Join(",", l.Values.Tiles.Select(t => t.Id))}")]),
        };
    }

    private async Task<string> OpenRestorePointAsync(TestDatabase here, Principal admin, string backup)
    {
        var status = await here.Get<IBackupImportSessions>().OpenFileAsync(admin.Id, backup, default);
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(admin.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        Assert.Equal(BackupImportState.Opened, here.Get<IBackupImportSessions>().Find(admin.Id, status.Token)!.State);
        return status.Token;
    }

    private static readonly ImportCarKinds Everything = new(Tyres: new ImportTyreChoice(null), Media: new ImportMediaChoice());

    [Theory, MemberData(nameof(Providers))]
    public async Task APurgedCar_RestoredFromARestorePoint_HasTheBackupsIdsOwnerCreatorsAndVersions_InEveryTable_AndReadsAsRestored(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await RestoreSourceAsync(here);
        var before = await RowsOfAsync(here, source.Car);
        // Carol no longer has an account: her own chart stays out, and so does her access.
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.Users.Where(u => u.Id == source.Carol.Id).ExecuteDeleteAsync();
        await AsAsync(here, source.Anna, async sp =>
        {
            await sp.GetRequiredService<VehicleService>().DeleteAsync(source.Car, default);
            return await sp.GetRequiredService<VehicleService>().EmptyTrashAsync(default);
        });

        var token = await OpenRestorePointAsync(here, source.Ada, source.Backup);
        var file = here.Get<IBackupImportSessions>().Cars(source.Ada.Id, token)!;
        var plan = new ImportRunPlan([new(source.Car, "Golf", null, null, RefusedEntryPolicy.ImportTheRest, 0, Everything)], Restore: true);
        await using var scope = here.Services.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<BackupImportWork>();
        ImportRunReport? report = null;
        await here.Get<IRunScope>().RunAsync(new RunStart(source.Ada, [source.Car], Restore: true),
            async run => report = await work.RunAsync(run, source.Ada, file, plan, _ => { }, default), default);

        var car = Assert.Single(report!.Cars);
        Assert.Empty(car.Refused);
        Assert.Equal((source.Anna.Id, true, 1, 1), (car.Restore!.OwnerId, car.Restore.CameBack, car.Restore.Grants, car.Restore.PeopleLeftOut));
        Assert.Equal(["Carol"], car.Restore.GoneGrantees);
        var after = await RowsOfAsync(here, source.Car);
        foreach (var table in before.Keys)
        {
            var expected = table switch
            {
                "VehicleCharts" => [.. before[table].Where(l => !l.Contains("Carol's"))],
                "ResourceGrants" => [.. before[table].Where(l => !l.StartsWith(source.Carol.Id.ToString()))],
                _ => before[table],
            };
            Assert.True(expected.SequenceEqual(after[table]), $"{table}:\n  backup:\n    {string.Join("\n    ", expected)}\n  restored:\n    {string.Join("\n    ", after[table])}");
        }
        // Every row the restore made reads as restored by the administrator.
        await using var check = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.All(await check.Expenses.IgnoreQueryFilters().Where(e => e.VehicleId == source.Car).ToListAsync(),
            e => Assert.Equal((EntityChange?)EntityChange.RestoredFromBackup, e.LastChange));
        Assert.All(await check.TyrePlaces.IgnoreQueryFilters().Where(e => e.VehicleId == source.Car).ToListAsync(),
            e => Assert.Equal((EntityChange.RestoredFromBackup, (Guid?)source.Ada.Id), (e.LastChange!.Value, e.ChangedById)));
        var vehicle = await check.Vehicles.SingleAsync(v => v.Id == source.Car);
        Assert.Equal((EntityChange?)EntityChange.RestoredFromBackup, vehicle.LastChange);
    }
}
