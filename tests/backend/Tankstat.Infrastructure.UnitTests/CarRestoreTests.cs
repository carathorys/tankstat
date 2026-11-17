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
using Tankstat.Domain.Imports;
using Tankstat.Domain.Notifications;
using Tankstat.Domain.Users;
using Tankstat.Domain.Charts;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Sync;
using Tankstat.Domain.Tyres;
using Tankstat.Domain.Vehicles;
using Tankstat.Domain;
using Tankstat.Infrastructure.Imports;
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

    /// <summary>A person's notifications once one of the kind is there (a run tells people after its row says how it ended), or after a while.</summary>
    private static async Task<List<Tankstat.Domain.Notifications.Notification>> ToldOfAsync(TestDatabase database, Guid recipient, NotificationKind kind)
    {
        for (var i = 0; ; i++)
        {
            var told = await NotificationsAsync(database, recipient);
            if (told.Any(t => t.Kind == kind) || i == 250) return told;
            await Task.Delay(20);
        }
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

    [Theory, MemberData(nameof(Providers))]
    public async Task ThroughTheQueue_EachCarByItsId_StillHereGetsWhatIsMissing_GoneComesToItsOwnerOrTheOnePicked_TrashedIsRefused_AndEveryoneIsTold(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var (anna, bob, dave, ada, eve) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Bob"), await PersonAsync(here, "Dave"),
            await PersonAsync(here, "Ada", admin: true), await PersonAsync(here, "Eve", admin: true));
        var (golf, fiat, gone) = await AsAsync(here, anna, async sp =>
        {
            var vehicles = sp.GetRequiredService<VehicleService>();
            var fills = sp.GetRequiredService<RefuelingService>();
            var golf = (await vehicles.AddAsync("Golf", "ABC-123", FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await fills.LogAsync(golf, new(Day, 40, 60, "EUR", 10_000, true, null), default);
            var gone = (await fills.LogAsync(golf, new(Day.AddDays(10), 38, 57, "EUR", 10_600, true, null), default)).Id;
            await sp.GetRequiredService<ResourceSharingService>().SetLogAccessAsync(golf, bob.Id, AccessLevel.Edit, default);
            var fiat = (await vehicles.AddAsync("Fiat", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            return (golf, fiat, gone);
        });
        var polo = await AsAsync(here, dave, async sp =>
        {
            var polo = (await sp.GetRequiredService<VehicleService>().AddAsync("Polo", null, FuelType.Diesel, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<RefuelingService>().LogAsync(polo, new(Day, 30, 45, "EUR", 5_000, true, null), default);
            return polo;
        });
        var backup = await here.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        // Since the backup: one of the Golf's fill-ups purged, the Fiat in the trash, the Polo purged and Dave's account gone; a new
        // account has his e-mail.
        await AsAsync(here, anna, async sp =>
        {
            await sp.GetRequiredService<RefuelingService>().DeleteAsync(gone, default);
            await sp.GetRequiredService<RefuelingService>().EmptyTrashAsync(default);
            return await sp.GetRequiredService<VehicleService>().DeleteAsync(fiat, default);
        });
        await AsAsync(here, dave, async sp =>
        {
            await sp.GetRequiredService<VehicleService>().DeleteAsync(polo, default);
            return await sp.GetRequiredService<VehicleService>().EmptyTrashAsync(default);
        });
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.Users.Where(u => u.Id == dave.Id).ExecuteDeleteAsync();
        var newDave = User.CreateLocal("dave@example.com", "Dave New", false);
        await here.Get<Tankstat.Application.Users.IUserRepository>().AddAsync(newDave, default);

        var sinceDevice = DateTimeOffset.UtcNow; // a device of Anna's downloaded the Golf now: the purged fill-up is gone for it
        var status = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().OpenAsync(Path.GetFileName(backup.Path), default));
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(ada.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        var page = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().CarsAsync(status.Token, null, null, 0, 50, default));
        RestoreCarOffer Offer(Guid id) => page.Cars.Single(c => c.Car.Id == id);
        Assert.Equal((RestoreCarState.Here, 1, anna.Id), (Offer(golf).State, Offer(golf).MissingRefuelings, Offer(golf).OwnerIdHere));
        Assert.Equal(1, Offer(golf).Grants);
        Assert.Equal((RestoreCarState.OwnerGone, (Guid?)newDave.Id), (Offer(polo).State, Offer(polo).SameEmailUserId));
        Assert.Equal(RestoreCarState.InTrash, Offer(fiat).State);
        // Only the administrators: a user's open file is not a way in.
        await Assert.ThrowsAsync<ForbiddenException>(() => AsAsync(here, anna, sp => sp.GetRequiredService<CarRestoreService>().CarsAsync(status.Token, null, null, 0, 50, default)));

        var review = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().ReviewAsync(status.Token, [new(golf, null), new(polo, null, OwnerId: newDave.Id)], default));
        Assert.Equal((RestoreCarState.Here, "Anna"), (review[0].Restore!.State, review[0].Restore!.OwnerName));
        Assert.Equal((RestoreCarState.OwnerGone, "Dave New"), (review[1].Restore!.State, review[1].Restore!.OwnerName));
        var run = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(golf, null), new(polo, null, OwnerId: newDave.Id)], default));
        Assert.Equal(ImportRunKind.Restore, run.Kind);
        Assert.Equal(ImportRunState.Done, (await FinishedAsync(here, run.Id)).State);

        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            Assert.True(await db.Refuelings.AnyAsync(r => r.Id == gone && r.CreatedById == anna.Id && r.LastChange == Tankstat.Domain.Sync.EntityChange.RestoredFromBackup));
            Assert.Equal(2, await db.Refuelings.CountAsync(r => r.VehicleId == golf));
            Assert.Equal(anna.Id, (await db.Vehicles.SingleAsync(v => v.Id == golf)).OwnerId);
            Assert.Equal(newDave.Id, (await db.Vehicles.SingleAsync(v => v.Id == polo)).OwnerId);
            Assert.Equal(dave.Id, (await db.Refuelings.SingleAsync(r => r.VehicleId == polo)).CreatedById); // who logged it, though gone
            Assert.Equal(newDave.Id, (await db.Refuelings.SingleAsync(r => r.VehicleId == polo)).OwnerId);
        }
        // Who is told, after the commit: each car's owner and sharers, the other administrators, the one who ran it.
        Assert.Contains(await ToldOfAsync(here, anna.Id, NotificationKind.CarRestored), n => n.Kind == NotificationKind.CarRestored && n.Args["how"] == "back" && n.Args["count"] == "1");
        Assert.Contains(await ToldOfAsync(here, bob.Id, NotificationKind.CarRestored), n => n.Kind == NotificationKind.CarRestored && n.Args["vehicleName"] == "Golf");
        Assert.Contains(await ToldOfAsync(here, newDave.Id, NotificationKind.CarRestored), n => n.Kind == NotificationKind.CarRestored && n.Args["how"] == "again");
        Assert.Contains(await ToldOfAsync(here, eve.Id, NotificationKind.CarsRestored), n => n.Kind == NotificationKind.CarsRestored && n.Args["count"] == "2");
        var mine = await ToldOfAsync(here, ada.Id, NotificationKind.RestoreFinished);
        Assert.Contains(mine, n => n.Kind == NotificationKind.RestoreFinished);
        Assert.DoesNotContain(mine, n => n.Kind is NotificationKind.CarRestored or NotificationKind.CarsRestored);
        // The Import page never shows a restore.
        Assert.Null(await AsAsync(here, ada, sp => sp.GetRequiredService<BackupImportRunService>().MineAsync(default)));
        // The device downloads it again: the restored fill-up is listed, and not removed, though its own tombstone is still there.
        var feed = await AsAsync(here, anna, sp => sp.GetRequiredService<Tankstat.Application.Sync.OfflineFeedService>().ChangesAsync(golf, null, sinceDevice, null, 200, default));
        Assert.Contains(feed.Refuelings, r => r.Id == gone);
        Assert.DoesNotContain(feed.Removed, r => r.Id == gone);
        // The app's own lines name ids only: no car, no person's name or e-mail (EF Core's SQL lines, silenced by the shipped settings, aside).
        var own = here.Log.Entries.Where(e => e.Category.StartsWith("Tankstat", StringComparison.Ordinal)).ToList();
        foreach (var canary in new[] { "Golf", "Polo", "Fiat", "ABC-123", "dave@example.com", "Dave New" })
            Assert.DoesNotContain(own, e => Tankstat.TestSupport.CapturedLog.Holds(e.Text, canary) || Tankstat.TestSupport.CapturedLog.Holds(e.ScopeText, canary));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task AReplaceAllOnAnotherPersonsCar_AsksForItsName_AndPutsTheBackupsEntriesBackAsTheyWere_TellingItsOwner(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var (anna, ada) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Ada", admin: true));
        var golf = await AsAsync(here, anna, async sp =>
        {
            var golf = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            var first = (await fills.LogAsync(golf, new(Day, 40, 60, "EUR", 10_000, true, null), default)).Id;
            await fills.UpdateAsync(first, new(Day, 41, 61, "EUR", 10_000, true, "edited"), default); // version 2
            await fills.LogAsync(golf, new(Day.AddDays(10), 38, 57, "EUR", 10_600, true, null), default);
            return golf;
        });
        var backup = await here.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var before = await RowsOfAsync(here, golf);
        await AsAsync(here, anna, async sp =>
        {
            var fills = sp.GetRequiredService<RefuelingService>();
            var all = await fills.ListAsync(golf, new RefuelingQuery(Take: 10), default);
            await fills.UpdateAsync(all.Single(r => r.Note == "edited").Id, new(Day, 42, 62, "EUR", 10_000, true, "again"), default);
            return await fills.LogAsync(golf, new(Day.AddDays(20), 30, 45, "EUR", 11_000, true, null), default);
        });

        var status = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().OpenAsync(Path.GetFileName(backup.Path), default));
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(ada.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        var replace = new ImportCarKinds(Refuelings: new ImportKindChoice(ImportKindMode.ReplaceAll), Tyres: new ImportTyreChoice([]), Media: new ImportMediaChoice(false, false, false, false, false));
        var unconfirmed = await Assert.ThrowsAsync<DomainException>(() => AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(golf, null, Kinds: replace)], default)));
        Assert.Equal(BackupImportRules.ReplaceNotConfirmedKey, unconfirmed.Key);
        var run = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(golf, null, Kinds: replace, ConfirmedName: "golf")], default));
        Assert.Equal(ImportRunState.Done, (await FinishedAsync(here, run.Id)).State);

        var after = await RowsOfAsync(here, golf);
        Assert.Equal(before["Refuelings"], after["Refuelings"]); // the backup's two, under their ids, by Anna, at their versions
        Assert.Equal(anna.Id, Assert.Single(after["Vehicles"].Select(l => Guid.Parse(l.Split(' ')[2]))));
        var told = Assert.Single(await ToldOfAsync(here, anna.Id, NotificationKind.CarRestored), n => n.Kind == NotificationKind.CarRestored);
        Assert.Equal(("replaced", "2", "refueling"), (told.Args["how"], told.Args["count"], told.Args["kinds"]));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarStillHereThatGetsNothingBack_TellsNobody_ButTheAdministrators(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var (anna, ada, eve) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Ada", admin: true), await PersonAsync(here, "Eve", admin: true));
        var golf = await AsAsync(here, anna, async sp =>
        {
            var golf = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<RefuelingService>().LogAsync(golf, new(Day, 40, 60, "EUR", 10_000, true, null), default);
            return golf;
        });
        var backup = await here.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        var status = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().OpenAsync(Path.GetFileName(backup.Path), default));
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(ada.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        var run = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(golf, null)], default));
        Assert.Equal(ImportRunState.Done, (await FinishedAsync(here, run.Id)).State);

        Assert.Contains(await ToldOfAsync(here, eve.Id, NotificationKind.CarsRestored), n => n.Kind == NotificationKind.CarsRestored);
        Assert.Empty(await NotificationsAsync(here, anna.Id)); // everything was here: the owner's answer on #340
    }

    public static TheoryData<string, bool> Elsewhere => new() { { "Sqlite", false }, { "PostgreSql", false }, { "SqlServer", false }, { "Sqlite", true } };

    [Theory, MemberData(nameof(Elsewhere))]
    public async Task ACarOfAnotherInstancesRestorePoint_ComesBackWhole_ToTheOwnerPicked_WithItsIdsAndItsTyresAsTheyWere(string provider, bool wholeSetLines)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await TyreSourceAsync(wholeSetLines);
        var (ada, zoe) = (await PersonAsync(here, "Ada", admin: true), await PersonAsync(here, "Zoe"));
        // Uploaded to the backups folder by hand: a restore point of this server now.
        var folder = Path.Combine(_root, "here", "backups");
        Directory.CreateDirectory(folder);
        File.Copy(source.Backup, Path.Combine(folder, Path.GetFileName(source.Backup)));

        var status = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().OpenAsync(Path.GetFileName(source.Backup), default));
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(ada.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        var offer = Assert.Single((await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().CarsAsync(status.Token, null, null, 0, 50, default))).Cars);
        Assert.Equal(RestoreCarState.OwnerGone, offer.State); // nobody of that instance has an account here
        // Nothing chosen for the tyres and the media: a car that comes back comes whole.
        var run = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(source.Car, null, OwnerId: zoe.Id)], default));
        var ended = await FinishedAsync(here, run.Id);
        Assert.Equal(ImportRunState.Done, ended.State);
        Assert.Empty(ImportRunJson.Read<ImportRunReport>(ended.Report)!.Cars.Single().Refused);

        var (theirs, ours) = (await RowsOfAsync(source.Database, source.Car), await RowsOfAsync(here, source.Car));
        // What was in the backup's trash does not come (#337): a time on the car moved there, with its line and its tyres' places.
        await using (var db = await source.Database.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var trashed = (await db.TyreInstallations.IgnoreQueryFilters().Where(i => i.VehicleId == source.Car && i.DeletedAt != null).Select(i => i.Id).ToListAsync())
                .Select(id => id.ToString()).ToList();
            foreach (var table in new[] { "TyreInstallations", "TyreEntries", "TyrePlaces" })
                theirs[table] = [.. theirs[table].Where(l => !trashed.Any(id => l.StartsWith(id, StringComparison.Ordinal) || l.Contains($" on {id} ", StringComparison.Ordinal)))];
        }
        foreach (var table in theirs.Keys.Where(t => t is not ("Dashboards" or "ResourceGrants" or "VehicleCharts" or "LogPhotos")))
        {
            // The owner is Zoe now; who logged each row is still Anna, who has no account here.
            List<string> actual = [.. ours[table].Select(l => l.Replace(zoe.Id.ToString(), source.Owner.Id.ToString())).Order(StringComparer.Ordinal)];
            Assert.True(theirs[table].SequenceEqual(actual), $"{table}:\n  theirs:\n    {string.Join("\n    ", theirs[table])}\n  ours:\n    {string.Join("\n    ", actual)}");
        }
        var (a, b) = (await AsAsync(source.Database, source.Owner, sp => sp.GetRequiredService<TyreService>().OverviewAsync(source.Car, default)),
            await AsAsync(here, zoe, sp => sp.GetRequiredService<TyreService>().OverviewAsync(source.Car, default)));
        Assert.Equal(Picture(a!), Picture(b!));
        Assert.Equal(await AsAsync(source.Database, source.Owner, sp => CostsAsync(sp, source.Car)), await AsAsync(here, zoe, sp => CostsAsync(sp, source.Car)));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACancelledRestore_LeavesNothing_AndTellsNoOwnerOrSharer(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var (anna, ada, eve) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Ada", admin: true), await PersonAsync(here, "Eve", admin: true));
        var golf = await AsAsync(here, anna, async sp =>
        {
            var golf = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<RefuelingService>().LogAsync(golf, new(Day, 40, 60, "EUR", 10_000, true, null), default);
            return golf;
        });
        var backup = await here.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        await AsAsync(here, anna, async sp =>
        {
            await sp.GetRequiredService<VehicleService>().DeleteAsync(golf, default);
            return await sp.GetRequiredService<VehicleService>().EmptyTrashAsync(default);
        });
        var status = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().OpenAsync(Path.GetFileName(backup.Path), default));
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(ada.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        var run = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(golf, null)], default));
        here.Get<BackupImportRunner>().Cancelling(run.Id, eve.Id);
        await here.Get<BackupImportRunner>().TickAsync(default);

        Assert.Equal((ImportRunState.NotFinished, ImportRunFailure.Cancelled), ((await FinishedAsync(here, run.Id)).State, (await FinishedAsync(here, run.Id)).Failure));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.False(await db.Vehicles.IgnoreQueryFilters().AnyAsync(v => v.Id == golf));
        Assert.Equal(NotificationKind.RestoreCancelled, Assert.Single(await ToldAsync(here, ada.Id)).Kind);
        await Task.Delay(200);
        Assert.Empty(await NotificationsAsync(here, anna.Id));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task AGoneCarWhoseOwnerHasNoAccountWhenItsTurnComes_IsLeftOutWithTheReason_AndAnUploadNamesNoOwnersEmail(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var (anna, ada, zoe) = (await PersonAsync(here, "Anna"), await PersonAsync(here, "Ada", admin: true), await PersonAsync(here, "Zoe"));
        var golf = await AsAsync(here, anna, async sp =>
        {
            var golf = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<RefuelingService>().LogAsync(golf, new(Day, 40, 60, "EUR", 10_000, true, null), default);
            return golf;
        });
        var backup = await here.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        // A person's upload of the same file names no owner's e-mail: the reduced users keep it for a restore point only.
        var uploaded = here.Get<IBackupImportSessions>().Cars(anna.Id, await OpenAsync(here, anna, backup.Path))!;
        Assert.All(uploaded.Summary.Cars, c => Assert.Null(c.OwnerEmail));
        await AsAsync(here, anna, async sp =>
        {
            await sp.GetRequiredService<VehicleService>().DeleteAsync(golf, default);
            return await sp.GetRequiredService<VehicleService>().EmptyTrashAsync(default);
        });
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.Users.Where(u => u.Id == anna.Id).ExecuteDeleteAsync();

        var status = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().OpenAsync(Path.GetFileName(backup.Path), default));
        for (var i = 0; i < 600 && here.Get<IBackupImportSessions>().Find(ada.Id, status.Token)!.State == BackupImportState.Opening; i++) await Task.Delay(50);
        Assert.Equal("anna@example.com", here.Get<IBackupImportSessions>().Cars(ada.Id, status.Token)!.Summary.Cars.Single().OwnerEmail);
        var run = await AsAsync(here, ada, sp => sp.GetRequiredService<CarRestoreService>().StartAsync(status.Token, [new(golf, null, OwnerId: zoe.Id)], default));
        // Zoe's account goes before the run gets its turn.
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.Users.Where(u => u.Id == zoe.Id).ExecuteDeleteAsync();
        var ended = await FinishedAsync(here, run.Id);

        var car = Assert.Single(ImportRunJson.Read<ImportRunReport>(ended.Report)!.Cars);
        Assert.Equal((true, "restore.ownerGone"), (car.LeftOut, Assert.Single(car.Refused).Key));
        await using var check = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.False(await check.Vehicles.IgnoreQueryFilters().AnyAsync(v => v.Id == golf));
    }
}
