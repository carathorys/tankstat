using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Auth;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Imports;
using Tankstat.Application.Photos;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Photos;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>The details of a merge (#338): each kind by its choice, Replace all, Take the backup's, and the visits' links.</summary>
public sealed partial class BackupImportRunTests
{
    private sealed record Golf(string Backup, Guid Id, Guid First, Guid Second, Guid Third);

    /// <summary>
    /// Another instance with Anna's Golf, and its backup: three fill-ups (1,000, 2,000 and 3,000), a wash, a service visit that marked the
    /// oil change done (the link), and two schedules (the oil change, the insurance).
    /// </summary>
    private async Task<Golf> GolfAsync()
    {
        var source = Instance("Sqlite", "golf")!;
        var anna = await PersonAsync(source, "Anna");
        var made = await AsAsync(source, anna, async sp =>
        {
            var golf = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", "GLF-1", FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            var first = (await fills.LogAsync(golf, new(Day, 40, 60, "EUR", 1_000, true, "first"), default)).Id;
            var second = (await fills.LogAsync(golf, new(Day.AddDays(10), 38, 57, "EUR", 2_000, true, "second"), default)).Id;
            var third = (await fills.LogAsync(golf, new(Day.AddDays(20), 39, 58, "EUR", 3_000, true, "third"), default)).Id;
            await sp.GetRequiredService<ExpenseService>().AddAsync(golf, new(Day.AddDays(2), "Wash", null, 12, "EUR", null, null), default);
            var recurring = sp.GetRequiredService<RecurringExpenseService>();
            var oil = (await recurring.AddAsync(golf, new("Oil change", null, null, RecurrenceKind.Time, 12, null, Day.AddYears(-1), null, null, null), default)).Item.Id;
            await recurring.AddAsync(golf, new("Insurance", null, null, RecurrenceKind.Time, 12, null, Day, null, null, null), default);
            await recurring.MarkDoneAsync([oil], new MarkDoneInput(Day.AddDays(5), null, 80, "EUR", "Service"), default);
            return (golf, first, second, third);
        });
        var backup = await source.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return new Golf(backup.Path, made.golf, made.first, made.second, made.third);
    }

    private static ImportKindReport Kind(ImportCarReport car, string kind) => car.Kinds!.Single(k => k.Kind == kind);

    private static async Task<ImportCarReport> ReportAsync(TestDatabase here, Guid runId)
    {
        var run = await FinishedAsync(here, runId);
        Assert.Equal((ImportRunState.Done, null), (run.State, run.Failure));
        return Assert.Single(ImportRunJson.Read<ImportRunReport>(run.Report)!.Cars);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task EntryByEntry_TheTickedNewOnesCome_TheOnesTakenGetTheBackupsValuesWhateverHappenedSince_AndAVisitKeepsItsLink_OnceOnly(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var golf = await GolfAsync();
        var bob = await PersonAsync(here, "Bob");
        var (own, mine, oil) = await AsAsync(here, bob, async sp =>
        {
            var own = (await sp.GetRequiredService<VehicleService>().AddAsync("My Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            var mine = (await fills.LogAsync(own, new(Day, 40, 60, "EUR", 1_000, true, "mine", null, 2), default)).Id; // the backup's first, with another note and a discount
            await fills.LogAsync(own, new(Day.AddDays(15), 30, 45, "EUR", 2_500, true, "only here"), default);
            await fills.DeleteAsync((await fills.LogAsync(own, new(Day.AddDays(30), 30, 45, "EUR", 4_000, true, null), default)).Id, default);
            await sp.GetRequiredService<ExpenseService>().AddAsync(own, new(Day.AddDays(2), "Wash", null, 12, "EUR", null, null), default); // the same, by its key
            var oil = (await sp.GetRequiredService<RecurringExpenseService>().AddAsync(own, new("Oil change", null, null, RecurrenceKind.Time, 6, null, Day.AddMonths(-2), null, null, null), default)).Item.Id;
            return (own, mine, oil);
        });
        var kinds = new ImportCarKinds(
            Refuelings: new(ImportKindMode.Choose, AddNew: true, NewExceptions: [golf.Third], TakeBackups: true), // all new but the third; what differs takes the backup's
            Schedules: new(ImportKindMode.Choose)); // new ones come, the oil change keeps mine
        var token = await OpenAsync(here, bob, golf.Backup);
        var imports = (IServiceProvider sp) => sp.GetRequiredService<BackupImportRunService>();

        // The review and the dialog's pages say it before anything is saved.
        var review = Assert.Single(await AsAsync(here, bob, sp => imports(sp).ReviewAsync(token, [new(golf.Id, own, Kinds: kinds)], default)));
        Assert.Equal(new ImportKindReview("refueling", ImportKindMode.Choose, Backup: 3, New: 2, Differ: 1, Same: 0, InTrash: 0, Adds: 1, Takes: 1, TargetLive: 2, TargetTrashed: 1, WithPhotos: 0, OnTyreLines: 0), review.Kinds[0]);
        Assert.Equal(new ImportKindReview("expense", ImportKindMode.OnlyMissing, 2, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0), review.Kinds[1]);
        Assert.Equal(new ImportKindReview("recurring", ImportKindMode.Choose, 2, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0), review.Kinds[2]);
        Assert.Equal((true, 0), (review.Links, review.LinksLost));
        var differing = await AsAsync(here, bob, sp => imports(sp).EntriesAsync(token, golf.Id, own, "refueling", differing: true, 0, 50, default));
        var entry = Assert.Single(differing.Items);
        Assert.Equal((1, golf.First, Day, 1_000L), (differing.Total, entry.Id, entry.Date, entry.Odometer));
        Assert.Equal([new ImportFieldDifference("note", "first", "mine"), new ImportFieldDifference("discount", null, "2")], entry.Differences);
        var fresh = await AsAsync(here, bob, sp => imports(sp).EntriesAsync(token, golf.Id, own, "refueling", differing: false, 1, 50, default));
        Assert.Equal((2, golf.Third), (fresh.Total, Assert.Single(fresh.Items).Id)); // the second page of one

        var queued = await StartAsync(here, bob, token, new ImportCarChoice(golf.Id, own, Kinds: kinds));
        // Edited between the review and the run: overwritten all the same (the owner's decision 7; no version is checked).
        await AsAsync(here, bob, sp => sp.GetRequiredService<RefuelingService>().UpdateAsync(mine, new(Day, 41, 61, "EUR", 1_000, true, "edited later"), default));
        var car = await ReportAsync(here, queued.Id);

        Assert.Equal((false, false, 1), (car.LeftOut, car.Replacing, car.Links));
        Assert.Equal(new ImportKindReport("refueling", ImportKindMode.Choose, Imported: 1, Changed: 1, Deleted: 0), Kind(car, "refueling"));
        Assert.Equal(new ImportKindReport("expense", ImportKindMode.OnlyMissing, 1, 0, 0), Kind(car, "expense"));
        Assert.Equal(new ImportKindReport("recurring", ImportKindMode.Choose, 1, 0, 0), Kind(car, "recurring"));
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var fills = await db.Refuelings.Include(r => r.OdometerReading).Include(r => r.Cost).Where(r => r.VehicleId == own).OrderBy(r => r.Date).ToListAsync();
            Assert.Equal([(1_000L, "first"), (2_000L, "second"), (2_500L, "only here")], fills.Select(r => (r.Odometer!.Value, r.Note!)));
            Assert.Equal((mine, 40m, 60m, null), (fills[0].Id, fills[0].Volume, fills[0].TotalCost, fills[0].Cost!.Discount)); // changed in place, the discount gone with the backup's none
            Assert.Equal(1, await db.Refuelings.IgnoreQueryFilters().CountAsync(r => r.VehicleId == own && r.DeletedAt != null)); // the trash is left
            Assert.Equal(["Service", "Wash"], await db.Expenses.Where(e => e.VehicleId == own).OrderBy(e => e.Title).Select(e => e.Title).ToListAsync());
            // The oil change kept mine and did not move: the link says the service marked it done, nothing more.
            var schedule = await db.RecurringExpenses.SingleAsync(s => s.Id == oil);
            Assert.Equal((6, Day.AddMonths(-2)), (schedule.IntervalMonths, schedule.LastDoneDate));
            var service = await db.Expenses.SingleAsync(e => e.VehicleId == own && e.Title == "Service");
            Assert.Equal((service.Id, oil), await db.RecurringCompletions.Select(c => new ValueTuple<Guid, Guid>(c.ExpenseId, c.RecurringExpenseId)).SingleAsync());
            Assert.Equal(2, await db.RecurringExpenses.CountAsync(s => s.VehicleId == own));
        }

        // The same again: the third stays unticked, everything else is there. Nothing is added, changed or linked.
        var again = await OpenAsync(here, bob, golf.Backup);
        var second = await ReportAsync(here, (await StartAsync(here, bob, again, new ImportCarChoice(golf.Id, own, Kinds: kinds))).Id);
        Assert.Equal((0, 0, 0, 0), (second.Refuelings, second.Expenses, second.Schedules, second.Links));
        Assert.All(second.Kinds!, k => Assert.Equal((0, 0, 0), (k.Imported, k.Changed, k.Deleted)));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ReplaceAll_LeavesExactlyTheBackupsEntriesOfThatKind_TheTrashEmptied_WhatWasThereThenDeleted_AndAKindLeftOutBringsNothing(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var golf = await GolfAsync();
        var bob = await PersonAsync(here, "Bob");
        var own = await AsAsync(here, bob, async sp =>
        {
            var own = (await sp.GetRequiredService<VehicleService>().AddAsync("My Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            await fills.LogAsync(own, new(Day.AddDays(1), 30, 45, "EUR", 1_500, true, "mine"), default);
            await fills.DeleteAsync((await fills.LogAsync(own, new(Day.AddDays(30), 30, 45, "EUR", 4_000, true, null), default)).Id, default);
            var recurring = sp.GetRequiredService<RecurringExpenseService>();
            var old = (await recurring.AddAsync(own, new("Old check", null, null, RecurrenceKind.Time, 3, null, Day.AddMonths(-1), null, null, null), default)).Item.Id;
            await recurring.MarkDoneAsync([old], new MarkDoneInput(Day.AddDays(3), null, 25, "EUR", "Visit"), default); // a visit of his own, with its link
            return own;
        });
        var kinds = new ImportCarKinds(Refuelings: new(ImportKindMode.ReplaceAll), Expenses: new(ImportKindMode.DontImport), Schedules: new(ImportKindMode.ReplaceAll));
        var token = await OpenAsync(here, bob, golf.Backup);
        Task<ImportRunView> Start(string? typed) => StartAsync(here, bob, token, new ImportCarChoice(golf.Id, own, Kinds: kinds, ConfirmedName: typed));

        // The review counts what goes, and the visit's link that will not be there (its schedule is replaced, and expenses are left out).
        var review = Assert.Single(await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(golf.Id, own, Kinds: kinds)], default)));
        Assert.Equal((ImportKindMode.ReplaceAll, 3, 1, 1), (review.Kinds[0].Mode, review.Kinds[0].Adds, review.Kinds[0].TargetLive, review.Kinds[0].TargetTrashed));
        Assert.Equal((false, 1), (review.Links, review.LinksLost));
        // The typed name is the server's to check too: missing or another car's is refused, its case and the spaces around it are not.
        Assert.Equal("import.replaceNotConfirmed", (await Assert.ThrowsAsync<DomainException>(() => Start(null))).Key);
        Assert.Equal("import.replaceNotConfirmed", (await Assert.ThrowsAsync<DomainException>(() => Start("Golf"))).Key);
        var queued = await Start("  my golf ");
        // Logged after the review: deleted with the rest (decision 8), and the report says how many went.
        await AsAsync(here, bob, sp => sp.GetRequiredService<RefuelingService>().LogAsync(own, new(Day.AddDays(40), 30, 45, "EUR", 9_000, true, "late"), default));
        var car = await ReportAsync(here, queued.Id);

        Assert.Equal((false, true, 0), (car.LeftOut, car.Replacing, car.Links));
        Assert.Equal(new ImportKindReport("refueling", ImportKindMode.ReplaceAll, Imported: 3, Changed: 0, Deleted: 3), Kind(car, "refueling"));
        Assert.Equal(new ImportKindReport("expense", ImportKindMode.DontImport, 0, 0, 0), Kind(car, "expense"));
        Assert.Equal(new ImportKindReport("recurring", ImportKindMode.ReplaceAll, 2, 0, 1), Kind(car, "recurring"));
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(["first", "second", "third"], await db.Refuelings.IgnoreQueryFilters().Where(r => r.VehicleId == own).OrderBy(r => r.Date).Select(r => r.Note!).ToListAsync());
        Assert.Equal(["Insurance", "Oil change"], await db.RecurringExpenses.Where(s => s.VehicleId == own).OrderBy(s => s.Title).Select(s => s.Title).ToListAsync());
        Assert.Equal(["Visit"], await db.Expenses.Where(e => e.VehicleId == own).Select(e => e.Title).ToListAsync()); // his expenses as they were, none of the backup's
        Assert.Equal(0, await db.RecurringCompletions.CountAsync()); // the old link went with its schedule; none came (expenses were left out)
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarWithAReplaceAll_IsAllOrNothing_ARefusedEntryOrAPhotoSinceTheReviewPutsItBackAsItWas_AndTheOtherCarsGoOn(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await SourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var (own, first) = await AsAsync(here, bob, async sp =>
        {
            var own = (await sp.GetRequiredService<VehicleService>().AddAsync("My Octavia", null, FuelType.Diesel, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            var first = (await fills.LogAsync(own, new(Day.AddDays(1), 30, 45, "EUR", 19_000, true, "mine"), default)).Id;
            // A reading of a kind that is kept: the backup's second fill-up (10,650 on the 11th) cannot come after it.
            await sp.GetRequiredService<ExpenseService>().AddAsync(own, new(Day.AddDays(5), "Tow", null, 30, "EUR", 20_000, null), default);
            await fills.LogAsync(own, new(Day.AddDays(20), 30, 45, "EUR", 21_000, true, "mine too"), default);
            return (own, first);
        });
        var octavia = new ImportCarChoice(source.Octavia, own, Kinds: new ImportCarKinds(Refuelings: new(ImportKindMode.ReplaceAll)), ConfirmedName: "My Octavia");
        async Task<List<string?>> NotesAsync()
        {
            await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await db.Refuelings.IgnoreQueryFilters().Where(r => r.VehicleId == own).OrderBy(r => r.Date).Select(r => r.Note).ToListAsync();
        }

        // A refused entry after the purge: the car is put back whole (the probe's case), the Fabia comes in.
        var run = await FinishedAsync(here, (await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), octavia, new ImportCarChoice(source.Fabia, null))).Id);
        var report = ImportRunJson.Read<ImportRunReport>(run.Report)!;
        var back = report.Cars[0];
        Assert.Equal((ImportRunState.Done, 1, true, true, null), (run.State, report.Imported, back.LeftOut, back.Replacing, back.Kinds));
        var refused = Assert.Single(back.Refused);
        Assert.Equal(("refueling", Day.AddDays(10), "odometer.belowPrevious"), (refused.Kind, refused.Date, refused.Key));
        Assert.Equal(["mine", "mine too"], await NotesAsync());
        Assert.False(report.Cars[1].LeftOut);

        // A photo on one of its refuellings since the review (decision 11): nothing is deleted, and the reason names the kind.
        var queued = await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), octavia);
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            db.LogPhotos.Add(LogPhoto.Create(bob.Id, own, LogType.Refueling, first, Guid.NewGuid(), bob.Id, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var held = ImportRunJson.Read<ImportRunReport>((await FinishedAsync(here, queued.Id)).Report)!.Cars.Single();
        var reason = Assert.Single(held.Refused);
        Assert.Equal((true, true, ImportRefusedEntry.Car, VehiclePurgeRefusals.HeldBackKey, "refueling"), (held.LeftOut, held.Replacing, reason.Kind, reason.Key, reason.Args["kind"]));
        Assert.Equal(["mine", "mine too"], await NotesAsync());
        // And from then on the start itself refuses it, as the page holds it back.
        var refusal = await Assert.ThrowsAsync<DomainException>(async () => await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), octavia));
        Assert.Equal((VehiclePurgeRefusals.HeldBackKey, 1), (refusal.Key, refusal.Args["photos"]));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ANewCar_BringsEachKindWholeOrNotAtAll_WithItsVisitsLinksWhenBothKindsCome_AndWhatCannotBeChosenIsRefusedAtTheStart(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var golf = await GolfAsync();
        var bob = await PersonAsync(here, "Bob");
        async Task<ImportCarReport> ImportAsync(ImportCarKinds? kinds) =>
            await ReportAsync(here, (await StartAsync(here, bob, await OpenAsync(here, bob, golf.Backup), new ImportCarChoice(golf.Id, null, Kinds: kinds))).Id);

        var whole = await ImportAsync(null);
        Assert.Equal((3, 2, 2, 1), (whole.Refuelings, whole.Expenses, whole.Schedules, whole.Links));
        var without = await ImportAsync(new ImportCarKinds(Schedules: new(ImportKindMode.DontImport)));
        Assert.Equal((3, 2, 0, 0), (without.Refuelings, without.Expenses, without.Schedules, without.Links)); // visits come without their link
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var link = await db.RecurringCompletions.SingleAsync();
            Assert.Equal(whole.VehicleId, (await db.Expenses.SingleAsync(e => e.Id == link.ExpenseId)).VehicleId);
            Assert.Equal(Day.AddDays(5), (await db.RecurringExpenses.SingleAsync(s => s.Id == link.RecurringExpenseId)).LastDoneDate); // as the backup has it: not moved again
        }

        var token = await OpenAsync(here, bob, golf.Backup);
        async Task<string> RefusedAsync(ImportCarChoice choice) => (await Assert.ThrowsAsync<DomainException>(() => StartAsync(here, bob, token, choice))).Key;
        var none = new ImportCarKinds(new(ImportKindMode.DontImport), new(ImportKindMode.DontImport), new(ImportKindMode.DontImport));
        Assert.Equal("import.nothingChosen", await RefusedAsync(new(golf.Id, null, Kinds: none)));
        Assert.Equal("import.nothingChosen", await RefusedAsync(new(golf.Id, whole.VehicleId, Kinds: none)));
        // Too many entries named one by one: the choices are kept in the run's row.
        var many = new ImportCarKinds(new(ImportKindMode.Choose, NewExceptions: [.. Enumerable.Range(0, BackupImportRules.MaxExceptions + 1).Select(_ => Guid.NewGuid())]));
        Assert.Equal("import.tooManyChoices", await RefusedAsync(new(golf.Id, whole.VehicleId, Kinds: many)));
        // A Replace all of a kind the backup has none of (the owner's decision 4): the Fabia of the other file has no expenses.
        var source = await SourceAsync();
        var other = await OpenAsync(here, bob, source.Backup);
        var nothing = await Assert.ThrowsAsync<DomainException>(() => StartAsync(here, bob, other,
            new ImportCarChoice(source.Fabia, without.VehicleId, Kinds: new ImportCarKinds(Expenses: new(ImportKindMode.ReplaceAll)), ConfirmedName: "Golf")));
        Assert.Equal("import.replaceNothing", nothing.Key);
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ChangesThatNeedEachOtherFirst_AreTriedAgain_TwoReadingsThatBothMoveUpComeThrough(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        // The backup: two visits at 2,200 and 2,500. Here they stand at 1,000 and 2,000: the first cannot go to 2,200 before the second left 2,000.
        var source = Instance("Sqlite", "moved")!;
        var anna = await PersonAsync(source, "Anna");
        var car = await AsAsync(source, anna, async sp =>
        {
            var id = (await sp.GetRequiredService<VehicleService>().AddAsync("Polo", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<ExpenseService>().AddAsync(id, new(Day, "Tyres", null, 10, "EUR", 2_200, null), default);
            await sp.GetRequiredService<ExpenseService>().AddAsync(id, new(Day.AddDays(10), "Brakes", null, 10, "EUR", 2_500, null), default);
            return id;
        });
        var backup = (await source.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default)).Path;
        var bob = await PersonAsync(here, "Bob");
        var own = await AsAsync(here, bob, async sp =>
        {
            var id = (await sp.GetRequiredService<VehicleService>().AddAsync("My Polo", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            await sp.GetRequiredService<ExpenseService>().AddAsync(id, new(Day, "Tyres", null, 10, "EUR", 1_000, null), default);
            await sp.GetRequiredService<ExpenseService>().AddAsync(id, new(Day.AddDays(10), "Brakes", null, 10, "EUR", 2_000, null), default);
            return id;
        });

        var report = await ReportAsync(here, (await StartAsync(here, bob, await OpenAsync(here, bob, backup),
            new ImportCarChoice(car, own, Kinds: new ImportCarKinds(Expenses: new(ImportKindMode.Choose, TakeBackups: true))))).Id);

        Assert.Empty(report.Refused);
        Assert.Equal(new ImportKindReport("expense", ImportKindMode.Choose, Imported: 0, Changed: 2, Deleted: 0), Kind(report, "expense"));
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal([2_200L, 2_500L], (await db.Expenses.Include(e => e.OdometerReading).Where(e => e.VehicleId == own).OrderBy(e => e.Date).ToListAsync()).Select(e => e.Odometer!.Value));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ALinkLost_IsOneOfTheCarsThatDoesNotComeBack_TheSameVisitComesBack_AndAReplacedKindLeavesNothingAsItWas(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var golf = await GolfAsync();
        var bob = await PersonAsync(here, "Bob");
        async Task<Guid> CarWithVisitAsync(string name, string schedule, string visit, decimal amount) => await AsAsync(here, bob, async sp =>
        {
            var own = (await sp.GetRequiredService<VehicleService>().AddAsync(name, null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            // The backup's first fill-up, with a note of its own: it differs.
            await sp.GetRequiredService<RefuelingService>().LogAsync(own, new(Day, 40, 60, "EUR", 1_000, true, "mine"), default);
            var recurring = sp.GetRequiredService<RecurringExpenseService>();
            var id = (await recurring.AddAsync(own, new(schedule, null, null, RecurrenceKind.Time, 12, null, Day.AddYears(-1), null, null, null), default)).Item.Id;
            await recurring.MarkDoneAsync([id], new MarkDoneInput(Day.AddDays(5), null, amount, "EUR", visit), default);
            return own;
        });
        var other = await CarWithVisitAsync("Other Golf", "Old check", "Visit", 25); // a visit the backup does not have
        var same = await CarWithVisitAsync("Same Golf", "Oil change", "Service", 80); // the backup's own visit
        var token = await OpenAsync(here, bob, golf.Backup);
        var kinds = new ImportCarKinds(Refuelings: new(ImportKindMode.ReplaceAll), Schedules: new(ImportKindMode.ReplaceAll));
        async Task<ImportCarReview> ReviewAsync(Guid target) =>
            Assert.Single(await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(golf.Id, target, Kinds: kinds)], default)));

        var lost = await ReviewAsync(other);
        Assert.Equal((true, 1), (lost.Links, lost.LinksLost)); // the Old check's link goes with it; the backup's Service brings back another one
        var kept = await ReviewAsync(same);
        Assert.Equal((true, 0), (kept.Links, kept.LinksLost)); // the Service and the Oil change are the same two: their link comes back
        // A replaced kind leaves nothing "as it is": the differing fill-up is not said to stay.
        Assert.Equal((0, 0, 0), (kept.ExistingRefuelings, kept.Differing, kept.InTrash));
        Assert.Equal(1, kept.ExistingExpenses); // expenses, only the missing: the Service is there already
    }
}
