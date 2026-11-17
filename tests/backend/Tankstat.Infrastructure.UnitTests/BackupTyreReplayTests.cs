using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tankstat.Application.Backups;
using Tankstat.Application.Expenses;
using Tankstat.Application.Imports;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Tyres;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Tyres;
using Tankstat.Domain.Vehicles;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.UnitTests;

/// <summary>A backup's tyre history replayed through the services (#339): the replayed car's tyres answer as the source car's.</summary>
public sealed partial class BackupImportRunTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    /// <summary>An amount as its value: the database servers give 615.00 where SQLite gives 615.0.</summary>
    private static decimal? N(decimal? amount) => amount / 1.000000000000000000000000000000m;

    private sealed record TyreSource(string Backup, Guid Car, TestDatabase Database, Tankstat.Application.Auth.Principal Owner);

    /// <summary>
    /// Anna's Golf: readings from fill-ups; a winter set with its rough figure, a swap with a note and a fee, tracked tyre by tyre with
    /// exact corners, a puncture of two tyres sharing an expense, W1 replaced by W5, positions changed afterwards (a rotation), the rough
    /// figure changed after the break-up; a summer set with a price and a reading on its swap; an all-season set retired in storage; a
    /// line of no set with an amount and a note of the summer set.
    /// </summary>
    /// <param name="wholeSetLines">Also lines for the whole winter set after it was broken up, one with an amount and one without, as the Seeder makes them.</param>
    private async Task<TyreSource> TyreSourceAsync(bool wholeSetLines = false)
    {
        var source = Instance("Sqlite", wholeSetLines ? "tyres-whole" : "tyres")!;
        var anna = await PersonAsync(source, "Anna");
        var car = await AsAsync(source, anna, async sp =>
        {
            var car = (await sp.GetRequiredService<VehicleService>().AddAsync("Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var fills = sp.GetRequiredService<RefuelingService>();
            foreach (var (day, km) in new[] { (D(2024, 10, 1), 10_000L), (D(2025, 3, 1), 15_000L), (D(2025, 11, 1), 20_000L), (D(2026, 6, 1), 25_000L) })
                await fills.LogAsync(car, new(day, 40, 60, "EUR", km, true, null), default);
            var tyres = sp.GetRequiredService<TyreService>();
            var expenses = sp.GetRequiredService<ExpenseService>();
            Guid winter = Guid.NewGuid(), summer = Guid.NewGuid(), old = Guid.NewGuid(), i1 = Guid.NewGuid();
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
            await expenses.AddAsync(car, new ExpenseInput(D(2025, 2, 1), "Puncture at Bob's", "Tyres", 20, "EUR", null, "Paid cash"), default, id: punct,
                tyre: new ExpenseTyreDetails(TyreEntryKind.PunctureRepaired, winter, [new TyreLine(w[0], l1), new TyreLine(w[1], l2)]));
            await tyres.UpdateEntryAsync(l1, new TyreEntryInput(TyreEntryKind.PunctureRepaired, winter, D(2025, 2, 1), null, "A nail", new TyreFee(20, "EUR", "Tyres", "Puncture at Bob's", punct), Also: [l2]), default);
            await tyres.ReplaceTyresAsync(new TyreReplaceInput(winter, D(2025, 12, 1), null, TyreRetiredWhy.Damaged, [new TyreReplacement(w[0], w[4], "W5", "Nokian new", 45, 2025)],
                new TyreFee(120, "EUR", "Tyres", "W5")), default);
            await tyres.ChangePositionsAsync(new TyreSwapInput(car, winter, D(2026, 1, 10), null, false, null, "Rotated",
                Corners: new Dictionary<Guid, TyreCorner?> { [w[4]] = TyreCorner.RearLeft, [w[1]] = TyreCorner.RearRight, [w[2]] = TyreCorner.FrontLeft, [w[3]] = TyreCorner.FrontRight }), default);
            await tyres.SetRoughAsync(winter, 9_000, D(2024, 11, 10), default);
            await tyres.AddSetAsync(new NewTyreSet(car, new TyreSetInput("Old all-season", TyreSeason.AllSeason, null, D(2019, 4, 1), null, null), false, D(2019, 4, 1), null, null), default, old);
            await tyres.RetireAsync(old, D(2024, 6, 1), default);
            await expenses.AddAsync(car, new ExpenseInput(D(2025, 6, 1), "Wheel alignment", "Tyres", 45, "EUR", null, null), default,
                tyre: new ExpenseTyreDetails(TyreEntryKind.Other, null, LineId: Guid.NewGuid()));
            await tyres.AddEntryAsync(car, new TyreEntryInput(TyreEntryKind.Note, summer, D(2025, 7, 1), null, "Pressure checked"), default);
            if (wholeSetLines)
            {
                await tyres.AddEntryAsync(car, new TyreEntryInput(TyreEntryKind.WheelBalancing, winter, D(2025, 2, 10), null, "All four"), default);
                await tyres.AddEntryAsync(car, new TyreEntryInput(TyreEntryKind.PunctureRepaired, winter, D(2025, 12, 20), null, "Rear left", new TyreFee(15, "EUR", "Tyres", "Puncture")), default);
                await tyres.AddEntryAsync(car, new TyreEntryInput(TyreEntryKind.StorageFee, winter, D(2024, 12, 1), null, null, new TyreFee(40, "EUR", "Tyres", "Storage")), default); // before the break-up
            }
            return car;
        });
        var backup = await source.Get<IBackupWriter>().WriteAsync(new BackupRequest(), default);
        return new TyreSource(backup.Path, car, source, anna);
    }

    /// <summary>A car's tyres as words, ids, versions, who logged and reminder dates aside: what the Tyres tab shows.</summary>
    private static List<string> Picture(TyreOverview tyres)
    {
        string Label(Guid? tyreId) => tyres.Sets.SelectMany(s => s.Tyres ?? []).FirstOrDefault(t => t.Tyre.Id == tyreId)?.Tyre.Label ?? "-";
        string SetName(Guid? setId) => tyres.Sets.FirstOrDefault(s => s.Set.Id == setId)?.Set.Name ?? "no set";
        var lines = new List<string>();
        foreach (var view in tyres.Sets.OrderBy(s => s.Set.Name))
        {
            var s = view.Set;
            lines.Add($"set {s.Name} {s.Season} {s.Size} {s.Since} {s.MadeWeek}/{s.MadeYear} rough {s.RoughDistance} until {s.RoughUntil} retired {s.RetiredOn} broken {s.BrokenUpOn} months {string.Join(",", s.Months.ToList())} reminder {s.ReminderMonth}");
            lines.Add($"  figures {view.Figures}");
            foreach (var i in view.Installations.OrderBy(i => i.Installation.From)) lines.Add($"  on {i.Installation.From}..{i.Installation.Until} pair {i.Installation.PairOneFront} {i.Distance}");
            foreach (var t in (view.Tyres ?? []).OrderBy(t => t.Tyre.Label))
            {
                lines.Add($"  tyre {t.Tyre.Label} {t.Tyre.Name} {t.Tyre.MadeWeek}/{t.Tyre.MadeYear} since {t.Tyre.Since} rough {t.Tyre.RoughDistance} until {t.Tyre.RoughUntil} retired {t.Tyre.RetiredOn} {t.Tyre.RetiredWhy} by {Label(t.Tyre.ReplacedById)} {t.Figures}");
                foreach (var p in t.Places.OrderBy(p => p.Place.From)) lines.Add($"    at {p.Place.From}..{p.Place.Until} {p.Place.Corner} {p.Distance}");
            }
        }
        foreach (var e in tyres.Entries.OrderBy(e => e.Day).ThenBy(e => e.Kind).ThenBy(e => Label(e.TyreId)))
            lines.Add($"line {e.Day} {e.Kind} {SetName(e.SetId)} {Label(e.TyreId)} odo {e.Odometer} note {e.Note} grouped {e.GroupId is not null} expense {e.ExpenseId is not null}");
        lines.Add($"spent {string.Join(",", (tyres.SpentAll ?? []).Select(x => $"{x.Currency} {N(x.Amount)}"))} no set {string.Join(",", (tyres.SpentNoSet ?? []).Select(x => $"{x.Currency} {N(x.Amount)}"))}");
        return lines;
    }

    /// <summary>A car's expenses as words: what the tyre log's amounts made, with what a fee cannot carry (a note).</summary>
    private static async Task<List<string>> CostsAsync(IServiceProvider sp, Guid car) =>
        [.. (await sp.GetRequiredService<ExpenseService>().ListAsync(car, new ExpenseQuery(Take: 100), default))
            .Select(e => $"{e.Date} {e.Title} {e.Category} {N(e.Amount)} {e.Currency} {N(e.Cost?.Discount)} odo {e.Odometer} note {e.Note}").Order()];

    public static TheoryData<string, bool> Histories => new() { { "Sqlite", false }, { "PostgreSql", false }, { "SqlServer", false }, { "Sqlite", true }, { "PostgreSql", true } };

    [Theory, MemberData(nameof(Histories))]
    public async Task ABackupsTyreHistory_ReplayedIntoACarWhoseLogsCameFirst_AnswersAsTheSourceCars(string provider, bool wholeSetLines)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await TyreSourceAsync(wholeSetLines);
        var bob = await PersonAsync(here, "Bob");
        var token = await OpenAsync(here, bob, source.Backup);
        var file = here.Get<IBackupImportSessions>().Cars(bob.Id, token)!;
        var book = (await file.ReadTyresAsync(source.Car, default))!;
        var expected = Picture((await AsAsync(source.Database, source.Owner, sp => sp.GetRequiredService<TyreService>().OverviewAsync(source.Car, default)))!);
        var expectedCosts = await AsAsync(source.Database, source.Owner, sp => CostsAsync(sp, source.Car));

        // The car's logs first (the import as #337 and #338 make it), so the distances come from the same readings.
        // Expenses are left out: every one of this car's is a tyre line's, which the tyres bring.
        var run = await FinishedAsync(here, (await StartAsync(here, bob, token, new ImportCarChoice(source.Car, null, Kinds: new ImportCarKinds(Expenses: new(ImportKindMode.DontImport))))).Id);
        var car = ImportRunJson.Read<ImportRunReport>(run.Report)!.Cars.Single().VehicleId!.Value;
        var replayed = await AsAsync(here, bob, async sp =>
        {
            var replay = sp.GetRequiredService<BackupTyreReplay>();
            foreach (var set in book.Sets.Where(s => !s.IsDeleted).OrderBy(s => s.Since)) await replay.SetAsync(car, book, set, default);
            await replay.LinesOfNoSetAsync(car, book, null, default);
            return (await sp.GetRequiredService<TyreService>().OverviewAsync(car, default))!;
        });

        var actual = Picture(replayed);
        output.WriteLine(string.Join("\n", actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expectedCosts, await AsAsync(here, bob, sp => CostsAsync(sp, car)));
    }

    private static async Task<int> ExpenseCountAsync(TestDatabase here, Guid car)
    {
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.Expenses.CountAsync(e => e.VehicleId == car);
    }

    private static async Task<List<string>> SetNamesAsync(TestDatabase here, Guid car)
    {
        await using var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.TyreSets.Where(s => s.VehicleId == car).OrderBy(s => s.Name).Select(s => s.Name).ToListAsync();
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ATyreCost_ComesOnce_WhicheverKindsAreChosen_AndAcrossTwoRuns(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await TyreSourceAsync(); // five expenses, every one a tyre line's
        var bob = await PersonAsync(here, "Bob");
        var token = await OpenAsync(here, bob, source.Backup);
        var all = (await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(source.Car, null)], default)))
            .Single().Tyres!.Sets.Select(s => s.Id).ToList();
        async Task<ImportCarReport> RunAsync(ImportCarChoice choice) =>
            ImportRunJson.Read<ImportRunReport>((await FinishedAsync(here, (await StartAsync(here, bob, await OpenAsync(here, bob, source.Backup), choice)).Id)).Report)!.Cars.Single();

        var both = await RunAsync(new(source.Car, null, Kinds: new ImportCarKinds(Tyres: new(Sets: null)))); // every new set, as the page starts
        Assert.Equal((5, 3, 0), (await ExpenseCountAsync(here, both.VehicleId!.Value), both.Tyres!.Sets.Count, both.Tyres.AmountsHere));
        Assert.Equal(0, both.Expenses); // every one came with its line
        var onlyTyres = await RunAsync(new(source.Car, null, Kinds: new ImportCarKinds(Expenses: new(ImportKindMode.DontImport), Tyres: new(all))));
        Assert.Equal((5, 5), (await ExpenseCountAsync(here, onlyTyres.VehicleId!.Value), onlyTyres.Tyres!.Expenses));
        // Every new set but one: the old all-season stays out.
        var but = await RunAsync(new(source.Car, null, Kinds: new ImportCarKinds(Expenses: new(ImportKindMode.DontImport), Tyres: new(null, NoSetLines: false, Except: [all[0]]))));
        Assert.Equal((2, 0), (but.Tyres!.Sets.Count, but.Tyres.NoSetLines));
        Assert.Equal(4, await ExpenseCountAsync(here, but.VehicleId!.Value)); // the alignment, a line of no set, stays out with its line
        var onlyExpenses = await RunAsync(new(source.Car, null));
        Assert.Equal((5, 5, null), (await ExpenseCountAsync(here, onlyExpenses.VehicleId!.Value), onlyExpenses.Expenses, onlyExpenses.Tyres));
        Assert.Empty(await SetNamesAsync(here, onlyExpenses.VehicleId!.Value));

        // The tyres later, into the car the expenses came into: the plain ones are found, never made again.
        var later = await RunAsync(new(source.Car, onlyExpenses.VehicleId, Kinds: new ImportCarKinds(Tyres: new(all))));
        Assert.Equal(5, await ExpenseCountAsync(here, onlyExpenses.VehicleId!.Value));
        Assert.Equal((3, 0, 3), (later.Tyres!.Sets.Count, later.Tyres.Expenses, later.Tyres.AmountsHere)); // the swap's fee, the price and the replacement's: left on their lines
        await using (var db = await here.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.Equal(2, await db.TyreEntries.Where(e => e.VehicleId == onlyExpenses.VehicleId && e.ExpenseId != null).Select(e => e.ExpenseId).Distinct().CountAsync()); // the puncture's and the alignment's tied

        // A second tyres import finds every set here.
        var again = Assert.Single(await AsAsync(here, bob, async sp => await sp.GetRequiredService<BackupImportRunService>().ReviewAsync(
            await OpenAsync(here, bob, source.Backup), [new(source.Car, onlyExpenses.VehicleId, Kinds: new ImportCarKinds(Tyres: new(all)))], default)));
        Assert.All(again.Tyres!.Sets, s => Assert.Equal(ImportSetState.Here, s.State));
        Assert.Equal((0, 0), (again.Tyres.NoSetLines, again.Tyres.Expenses));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ASetThatCollidesWithTheCarsHistory_IsForeseenInTheReview_AndRefusedInTheRun_WhileTheOthersCome(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await TyreSourceAsync();
        var bob = await PersonAsync(here, "Bob");
        // Bob's own Golf had its own winter set on from November 2024 to March 2025: the backup's Nokian WR was on then too.
        var own = await AsAsync(here, bob, async sp =>
        {
            var car = (await sp.GetRequiredService<VehicleService>().AddAsync("My Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id;
            var tyres = sp.GetRequiredService<TyreService>();
            var mine = Guid.NewGuid();
            await tyres.AddSetAsync(new NewTyreSet(car, new TyreSetInput("Hankook", TyreSeason.Winter, null, D(2024, 10, 1), null, null), false, D(2024, 11, 1), null, null), default, mine);
            await tyres.AddInstallationAsync(mine, new TyreInstallationInput(D(2024, 11, 1), D(2025, 3, 1), null, null, null), default);
            return car;
        });
        var token = await OpenAsync(here, bob, source.Backup);
        var sets = Assert.Single(await AsAsync(here, bob, sp => sp.GetRequiredService<BackupImportRunService>().ReviewAsync(token, [new(source.Car, own)], default))).Tyres!.Sets;
        var nokian = sets.Single(s => s.Name == "Nokian WR");
        Assert.Equal((ImportSetState.Refused, "tyres.twoSetsAtOnce"), (nokian.State, nokian.RefusalKey));
        Assert.All(sets.Where(s => s != nokian), s => Assert.Equal(ImportSetState.New, s.State));
        Assert.Equal((3, 6, 5, 3), (nokian.Installations, nokian.Lines, nokian.Tyres, nokian.Expenses));

        // The others chosen; one that collides only since the review is refused in the run, said in the report, and the others come.
        var choice = new ImportCarChoice(source.Car, own, Kinds: new ImportCarKinds(Tyres: new([.. sets.Where(s => s != nokian).Select(s => s.Id)])));
        var queued = await StartAsync(here, bob, token, choice);
        await AsAsync(here, bob, async sp =>
        {
            var tyres = sp.GetRequiredService<TyreService>();
            var late = Guid.NewGuid();
            await tyres.AddSetAsync(new NewTyreSet(own, new TyreSetInput("Late summer", TyreSeason.Summer, null, D(2025, 1, 1), null, null), false, D(2025, 4, 1), null, null), default, late);
            return await tyres.AddInstallationAsync(late, new TyreInstallationInput(D(2025, 4, 1), D(2025, 10, 1), null, null, null), default);
        });
        var report = ImportRunJson.Read<ImportRunReport>((await FinishedAsync(here, queued.Id)).Report)!.Cars.Single();
        Assert.False(report.LeftOut);
        var refused = Assert.Single(report.Refused);
        Assert.Equal((BackupImportWork.TyreSetKind, "Michelin Primacy", "tyres.twoSetsAtOnce"), (refused.Kind, refused.Title, refused.Key));
        Assert.Equal(["Old all-season"], report.Tyres!.Sets);
        Assert.Equal(["Hankook", "Late summer", "Old all-season"], await SetNamesAsync(here, own));
    }

    [Theory, MemberData(nameof(Providers))]
    public async Task ACarBringsOnlyItsTyres_AndTheChooseDialogDoesNotOfferTheExpensesTheTyresBring(string provider)
    {
        if (Instance(provider, "here") is not { } here) return;
        var source = await TyreSourceAsync();
        var bob = await PersonAsync(here, "Bob");
        var own = await AsAsync(here, bob, async sp => (await sp.GetRequiredService<VehicleService>().AddAsync("My Golf", null, FuelType.Petrol, MeasurementUnits.Metric, default)).Id);
        var token = await OpenAsync(here, bob, source.Backup);
        var imports = (IServiceProvider sp) => sp.GetRequiredService<BackupImportRunService>();
        var nothing = new ImportKindChoice(ImportKindMode.DontImport);
        var all = new ImportTyreChoice(null);

        // Every log kind left out: no import without tyres, one with them.
        var none = await Assert.ThrowsAsync<DomainException>(() => AsAsync(here, bob, sp => imports(sp).ReviewAsync(token, [new(source.Car, own, Kinds: new(nothing, nothing, nothing))], default)));
        Assert.Equal("import.nothingChosen", none.Key);
        var onlyTyres = Assert.Single(await AsAsync(here, bob, sp => imports(sp).ReviewAsync(token, [new(source.Car, own, Kinds: new(nothing, nothing, nothing, all))], default)));
        Assert.Equal(5, onlyTyres.Tyres!.Expenses);

        // The dialog's expenses: with the tyres chosen, the five their lines hold are not among them.
        var with = await AsAsync(here, bob, sp => imports(sp).EntriesAsync(token, source.Car, own, "expense", differing: false, 0, 50, default, all));
        var without = await AsAsync(here, bob, sp => imports(sp).EntriesAsync(token, source.Car, own, "expense", differing: false, 0, 50, default));
        Assert.Equal((0, 5), (with.Total, without.Total));
    }
}
