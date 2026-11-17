using System.Globalization;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Runs;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>
/// One kind of a backup's car against its target (#338). Of the <see cref="Backup"/>'s entries: <see cref="New"/> are only there and not
/// in the target's trash either, <see cref="Differ"/> and <see cref="Same"/> are the target's (each with the target's entry),
/// <see cref="InTrash"/> match its trash (said and left). <see cref="TargetIds"/>: the target's live entries of the kind;
/// <see cref="WithPhotos"/> and <see cref="OnTyreLines"/>: those, live or trashed, that hold a <i>Replace all</i> back.
/// </summary>
public sealed record ImportKindComparison<T>(
    IReadOnlyList<T> Backup, IReadOnlyList<T> New, IReadOnlyList<BackupEntryComparison<T>> Differ, IReadOnlyList<BackupEntryComparison<T>> Same,
    int InTrash, IReadOnlyList<Guid> TargetIds, int TargetTrashed, int WithPhotos, int OnTyreLines) where T : IBackupEntry
{
    public bool HeldBack => WithPhotos + OnTyreLines > 0;
}

/// <summary>A backup's car against its target as it is now; <see cref="TargetLinks"/>: the target's visits' links (an expense, live or trashed, and a schedule it marked done).</summary>
public sealed record ImportCarComparison(
    ImportKindComparison<BackupRefueling> Refuelings, ImportKindComparison<BackupExpense> Expenses, ImportKindComparison<BackupSchedule> Schedules,
    IReadOnlyList<(Guid ExpenseId, Guid ScheduleId)> TargetLinks, bool NewCar);

/// <summary>
/// What a kind's choice does: the entries to add, the target's entries that take the backup's values, whether the target's are deleted
/// for good first, and <see cref="Counterparts"/>: the backup's entries that are an entry of the target's after the run without being
/// added (the same, or differing, whoever's values it keeps), by the backup's id.
/// </summary>
public sealed record ImportKindWork<T>(ImportKindMode Mode, IReadOnlyList<T> Adds, IReadOnlyList<(T Entry, Guid TargetId)> Takes, bool Purge, IReadOnlyDictionary<Guid, Guid> Counterparts)
    where T : IBackupEntry;

/// <summary>
/// What a car's choices do (#338), worked out the same way for the Cars step, the review, the confirmation and the run.
/// <see cref="Links"/>: visits keep their link (neither expenses nor recurring expenses are left out). <see cref="LinksLost"/>: the
/// target's links that will not be there afterwards (the owner's decision 12).
/// </summary>
public sealed record ImportCarWork(
    ImportKindWork<BackupRefueling> Refuelings, ImportKindWork<BackupExpense> Expenses, ImportKindWork<BackupSchedule> Schedules, bool Links, int LinksLost)
{
    public int Entries => Refuelings.Adds.Count + Refuelings.Takes.Count + Expenses.Adds.Count + Expenses.Takes.Count + Schedules.Adds.Count + Schedules.Takes.Count;
    public bool Replacing => Refuelings.Purge || Expenses.Purge || Schedules.Purge;
}

/// <summary>
/// The rules of an import from a backup that the review and the run share (#337, #338), so the run does what the review said, as far as
/// the cars stayed as they were: which cars may be targets, how a backup's car compares with its target, and what each kind's choice does.
/// </summary>
/// <summary>
/// A car's tyres against the car they go into (#339): the backup's book, each live set with what it is to the car, and the lines of no set
/// to come. <see cref="ExpensesOf"/>: the backup's expenses the chosen tyres bring with their lines, which the expense step leaves to them.
/// </summary>
public sealed record ImportTyreComparison(Tyres.TyreBook Backup, IReadOnlyList<(Domain.Tyres.TyreSet Set, ImportSetOffer Offer)> Sets, IReadOnlyList<Domain.Tyres.TyreEntry> NoSetLines)
{
    public static readonly ImportTyreComparison None = new(new Tyres.TyreBook([], [], [], []), [], []);

    public IReadOnlyList<Domain.Tyres.TyreSet> Chosen(ImportTyreChoice? choice) =>
        choice is null ? [] : [.. Sets.Where(s => s.Offer.State == ImportSetState.New && choice.Wants(s.Set.Id)).Select(s => s.Set)];

    public IReadOnlySet<Guid> ExpensesOf(ImportTyreChoice? choice)
    {
        if (choice is null) return new HashSet<Guid>();
        var chosen = Chosen(choice).Select(s => s.Id).ToHashSet();
        return Backup.Entries.Where(e => Backup.IsLive(e) && e.ExpenseId is not null && ((e.SetId is { } set && chosen.Contains(set))
                || (choice.NoSetLines && e.SetId is null && NoSetLines.Contains(e))) && Backup.ExpenseOf(e) is { IsDeleted: false })
            .Select(e => e.ExpenseId!.Value).ToHashSet();
    }
}

public sealed class BackupImportRules(IImportTargetReader targets, IVehicleRepository vehicles, Tyres.ITyreRepository tyreBooks)
{
    public const string UnitsDifferKey = "import.carUnitsDiffer";
    public const string NothingChosenKey = "import.nothingChosen";
    public const string ReplaceNothingKey = "import.replaceNothing";
    public const string ReplaceNotConfirmedKey = "import.replaceNotConfirmed";
    public const string TooManyChoicesKey = "import.tooManyChoices";

    /// <summary>The ids a run's choices may name as exceptions, all cars together: they are kept in the run's plan.</summary>
    public const int MaxExceptions = 10_000;

    public const string Refueling = "refueling", Expense = "expense", Recurring = "recurring";

    /// <summary>
    /// The target of a choice: a live car the person <b>owns</b> (one only shared with them, or an administrator's reach into someone
    /// else's, looks missing: <c>vehicle.notFound</c>), in the backup car's units (nothing is converted: <c>import.unitsDiffer</c>).
    /// </summary>
    public async Task<Vehicle> TargetAsync(Guid ownerId, BackupCarSummary car, Guid targetId, CancellationToken ct)
    {
        var target = await vehicles.FindAsync(targetId, ct);
        if (target is null || target.OwnerId != ownerId) throw new NotFoundException("vehicle.notFound", $"Vehicle {targetId} does not exist.", new { Id = targetId });
        if (target.Units != car.Units)
            throw new DomainException(UnitsDifferKey, "The backup's car and the target are in other units; nothing is converted.", new { Car = car.Name, Target = target.Name });
        return target;
    }

    /// <summary>The choices as a whole: every car is the file's, none twice, and no car of the person's is the target of two.</summary>
    public static void RequireSound(IReadOnlyCollection<ImportCarChoice> choices, BackupFileSummary file)
    {
        if (choices.Count == 0) throw new DomainException("import.noCars", "Choose at least one car to import.");
        if (choices.Sum(c => (c.Kinds ?? ImportCarKinds.Missing).All.Sum(k => k.Exceptions)) > MaxExceptions)
            throw new DomainException(TooManyChoicesKey, "Too many entries were chosen one by one.", new { Max = MaxExceptions });
        if (choices.Any(c => file.Cars.All(f => f.Id != c.CarId))) throw new DomainException("import.unknownCar", "The file has no such car.");
        if (choices.GroupBy(c => c.CarId).Any(g => g.Count() > 1) || choices.Where(c => c.TargetVehicleId is not null).GroupBy(c => c.TargetVehicleId).Any(g => g.Count() > 1))
            throw new DomainException("import.carTwice", "A car can be chosen once, and each of your cars can take one car of the file.");
    }

    /// <summary>
    /// The backup's car against its target as it is now. What is only in the backup but matches an entry in the target's trash (by id,
    /// else by key, like the live ones) is counted apart: said and left (#337's decision 1), unless the kind is replaced.
    /// </summary>
    public async Task<ImportCarComparison> CompareAsync(BackupCar car, Vehicle target, CancellationToken ct)
    {
        var (live, trash) = (await targets.LiveAsync(target, ct), await targets.TrashAsync(target, ct));
        var held = await targets.HeldBackAsync(target, ct);
        var compared = BackupCarComparer.Compare(car, live);
        var onlyThere = car with
        {
            Refuelings = [.. compared.Refuelings.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            Expenses = [.. compared.Expenses.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            Schedules = [],
        };
        var trashed = BackupCarComparer.Compare(onlyThere, trash);
        return new ImportCarComparison(
            Kind(car.Refuelings, compared.Refuelings, trashed.Refuelings, live.Refuelings, trash.Refuelings.Count, held.RefuelingPhotos, 0),
            Kind(car.Expenses, compared.Expenses, trashed.Expenses, live.Expenses, trash.Expenses.Count, held.ExpensePhotos, held.ExpenseTyreLines),
            Kind(car.Schedules, compared.Schedules, null, live.Schedules, 0, 0, 0),
            [.. live.Expenses.Concat(trash.Expenses).SelectMany(e => e.CompletedScheduleIds.Select(s => (e.Id, s)))], NewCar: false);
    }

    private static ImportKindComparison<T> Kind<T>(
        IReadOnlyList<T> backup, BackupKindComparison<T> compared, BackupKindComparison<T>? trashed, IReadOnlyList<T> live, int inTrashOfTarget, int photos, int lines) where T : IBackupEntry
    {
        var onlyThere = compared.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry).ToList();
        IReadOnlyList<T> fresh = trashed is null ? onlyThere : [.. trashed.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)];
        return new ImportKindComparison<T>(
            backup, fresh, [.. compared.Entries.Where(e => e.Match == BackupEntryMatch.Differs)], [.. compared.Entries.Where(e => e.Match == BackupEntryMatch.Same)],
            onlyThere.Count - fresh.Count, [.. live.Select(e => e.Id)], inTrashOfTarget, photos, lines);
    }

    /// <summary>
    /// A backup car's tyres against the car they go into (null: a new car). <b>The same set</b> is the one with its id in the car, else the
    /// one with its name, season, size and <i>in use since</i> month (without case); two of the backup's on one: the first by <i>since</i>
    /// then id takes it. A set here stays as it is. A new set the car's history would refuse (another set on the car then) or whose tyres'
    /// labels the car's tyres have is refused, with the reason the services give. In a car that is here, only the missing lines of no set
    /// come (by id, else by day, kind and amount).
    /// </summary>
    public async Task<ImportTyreComparison> CompareTyresAsync(IBackupCars file, Guid carId, Vehicle? target, CancellationToken ct)
    {
        var book = await file.ReadTyresAsync(carId, ct) ?? ImportTyreComparison.None.Backup;
        var here = target is null ? null : await tyreBooks.LoadAsync(target.Id, ct);
        var hereSets = here?.Sets.Where(s => !s.IsDeleted).ToList() ?? [];
        static string Key(Domain.Tyres.TyreSet s) =>
            $"{s.Name.Trim().ToLowerInvariant()}|{s.Season}|{s.Size?.Trim().ToLowerInvariant()}|{s.Since.Year}-{s.Since.Month}";
        var taken = new HashSet<Guid>();
        var sets = new List<(Domain.Tyres.TyreSet, ImportSetOffer)>();
        foreach (var set in book.Sets.Where(s => !s.IsDeleted).OrderBy(s => s.Since).ThenBy(s => s.Id))
        {
            var lines = book.Entries.Where(e => e.SetId == set.Id && book.IsLive(e)).ToList();
            var installations = book.Installations.Where(i => i.SetId == set.Id && !i.IsDeleted).ToList();
            var singles = book.SingleTyres.Where(t => t.SetId == set.Id && !t.IsDeleted).ToList();
            var expenses = lines.Where(l => book.ExpenseOf(l) is { IsDeleted: false }).Select(l => l.ExpenseId).Distinct().Count();
            var match = hereSets.FirstOrDefault(s => s.Id == set.Id && taken.Add(s.Id)) ?? hereSets.FirstOrDefault(s => Key(s) == Key(set) && taken.Add(s.Id));
            var (state, key, args) = (ImportSetState.New, (string?)null, (IReadOnlyDictionary<string, string>?)null);
            if (match is not null) state = ImportSetState.Here;
            else if (here is not null && target is not null)
            {
                try
                {
                    // The car's history with this set's times on the car added: the checks every change of it meets.
                    new Domain.Tyres.TyreHistory(target.OwnerId, target.Id, [.. hereSets, set], [.. here.Installations.Where(i => !i.IsDeleted), .. installations]).Check(installations);
                    var labels = here.SingleTyres.Where(t => !t.IsDeleted && hereSets.Any(s => s.Id == t.SetId)).Select(t => t.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (singles.FirstOrDefault(t => labels.Contains(t.Label)) is { } clash)
                        throw new DomainException("tyres.labelTaken", "Another tyre is already called that.", new { Label = clash.Label });
                }
                catch (DomainException e)
                {
                    (state, key, args) = (ImportSetState.Refused, e.Key, e.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                }
            }
            sets.Add((set, new ImportSetOffer(set.Id, BackupCarShapes.Cut(set.Name)!, set.Season, state, installations.Count, lines.Count, singles.Count, expenses, key, args)));
        }
        var noSet = book.Entries.Where(e => e.SetId is null && book.IsLive(e)).ToList();
        if (here is not null)
        {
            var hereLines = here.Entries.Where(e => e.SetId is null && here.IsLive(e)).Select(e => (e.Id, e.Day, e.Kind, Amount: here.ExpenseOf(e)?.Amount)).ToList();
            noSet = [.. noSet.Where(l => !hereLines.Any(h => h.Id == l.Id || (h.Day == l.Day && h.Kind == l.Kind && h.Amount == book.ExpenseOf(l)?.Amount)))];
        }
        return new ImportTyreComparison(book, sets, noSet);
    }

    /// <summary>A car that comes as a new one has nothing to compare with: everything of it is new.</summary>
    public static ImportCarComparison Whole(BackupCar car)
    {
        static ImportKindComparison<T> All<T>(IReadOnlyList<T> entries) where T : IBackupEntry => new(entries, entries, [], [], 0, [], 0, 0, 0);
        return new ImportCarComparison(All(car.Refuelings), All(car.Expenses), All(car.Schedules), [], NewCar: true);
    }

    /// <summary>
    /// What the choices do with the car as it compares now. A new car's kind comes whole or not at all. A visit's link comes only when
    /// neither expenses nor recurring expenses are left out, between entries that are both the car's afterwards.
    /// </summary>
    /// <param name="tyreExpenses">The backup's expenses the chosen tyres bring with their lines (#339): the expense step leaves them out.</param>
    public static ImportCarWork Work(ImportCarComparison compared, ImportCarKinds? kinds, IReadOnlySet<Guid>? tyreExpenses = null)
    {
        kinds ??= ImportCarKinds.Missing;
        if (tyreExpenses is { Count: > 0 })
            compared = compared with
            {
                Expenses = compared.Expenses with
                {
                    New = [.. compared.Expenses.New.Where(e => !tyreExpenses.Contains(e.Id))],
                    Backup = [.. compared.Expenses.Backup.Where(e => !tyreExpenses.Contains(e.Id))],
                },
            };
        var refuelings = Kind(compared.Refuelings, kinds.Refuelings, compared.NewCar);
        var expenses = Kind(compared.Expenses, kinds.Expenses, compared.NewCar);
        var schedules = Kind(compared.Schedules, kinds.Schedules, compared.NewCar);
        var links = expenses.Mode != ImportKindMode.DontImport && schedules.Mode != ImportKindMode.DontImport;
        var lost = 0;
        if (expenses.Purge || schedules.Purge)
        {
            // Every link of the car goes with a replaced kind (a schedule's links go with it, an expense's with it), less the ones the
            // run adds back: a link of the backup's between the same two entries (matched as the comparison matches them), both of
            // which come in.
            var (comingExpenses, comingSchedules) = (Coming(expenses), Coming(schedules));
            var (expenseMatch, scheduleMatch) = (Matches(compared.Expenses), Matches(compared.Schedules));
            var back = !links ? [] : compared.Expenses.Backup
                .Where(e => comingExpenses.Contains(e.Id) && expenseMatch.ContainsKey(e.Id))
                .SelectMany(e => e.CompletedScheduleIds.Where(s => comingSchedules.Contains(s) && scheduleMatch.ContainsKey(s)).Select(s => (expenseMatch[e.Id], scheduleMatch[s])))
                .ToHashSet();
            lost = compared.TargetLinks.Count(l => !back.Contains(l));
        }
        return new ImportCarWork(refuelings, expenses, schedules, links, lost);
    }

    private static HashSet<Guid> Coming<T>(ImportKindWork<T> kind) where T : IBackupEntry => [.. kind.Adds.Select(e => e.Id), .. kind.Counterparts.Keys];

    /// <summary>The backup's entries the comparison matched with one of the target's (the same, or differing), by the backup's id.</summary>
    private static Dictionary<Guid, Guid> Matches<T>(ImportKindComparison<T> kind) where T : IBackupEntry =>
        kind.Differ.Concat(kind.Same).ToDictionary(e => e.Entry.Id, e => e.TargetId!.Value);

    /// <summary>
    /// What of a kind is left as it is by its choice, as the review and the report say it: the entries that are the same, the ones that
    /// differ and are not taken, the ones that match the target's trash. A kind left out or replaced leaves none of these.
    /// </summary>
    public static (int Same, int Differ, int InTrash) Left<T>(ImportKindComparison<T> kind, ImportKindWork<T> work) where T : IBackupEntry =>
        work.Mode is ImportKindMode.OnlyMissing or ImportKindMode.Choose ? (kind.Same.Count, kind.Differ.Count - work.Takes.Count, kind.InTrash) : (0, 0, 0);

    private static ImportKindWork<T> Kind<T>(ImportKindComparison<T> compared, ImportKindChoice? choice, bool newCar) where T : IBackupEntry
    {
        choice ??= ImportKindChoice.Missing;
        var mode = newCar && choice.Mode != ImportKindMode.DontImport ? ImportKindMode.OnlyMissing : choice.Mode;
        var counterparts = compared.Differ.Concat(compared.Same).ToDictionary(e => e.Entry.Id, e => e.TargetId!.Value);
        switch (mode)
        {
            case ImportKindMode.DontImport:
                return new ImportKindWork<T>(mode, [], [], false, counterparts);
            case ImportKindMode.ReplaceAll:
                return new ImportKindWork<T>(mode, compared.Backup, [], true, new Dictionary<Guid, Guid>()); // every entry of the backup is new to the emptied kind
            case ImportKindMode.Choose:
                var (unpicked, flipped) = (choice.NewExceptions?.ToHashSet() ?? [], choice.DifferExceptions?.ToHashSet() ?? []);
                return new ImportKindWork<T>(mode,
                    [.. compared.New.Where(e => choice.AddNew != unpicked.Contains(e.Id))],
                    [.. compared.Differ.Where(e => choice.TakeBackups != flipped.Contains(e.Entry.Id)).Select(e => (e.Entry, e.TargetId!.Value))],
                    false, counterparts);
            default:
                return new ImportKindWork<T>(mode, compared.New, [], false, counterparts);
        }
    }

    /// <summary>
    /// What can be refused before a run is queued (the page holds the same back): a car with every kind left out, and a <i>Replace all</i>
    /// of a kind that is held back (a photo, a tyre log line) or that the backup has none of.
    /// </summary>
    public static void RequireSound(BackupCarSummary car, ImportCarComparison compared, ImportCarKinds? kinds)
    {
        kinds ??= ImportCarKinds.Missing;
        if (kinds.All.All(k => k.Mode == ImportKindMode.DontImport) && !kinds.AnyTyres)
            throw new DomainException(NothingChosenKey, "Choose at least one kind to import for this car, or leave the car out.", new { Car = car.Name });
        if (compared.NewCar) return;
        Check(compared.Refuelings, kinds.Refuelings);
        Check(compared.Expenses, kinds.Expenses);
        Check(compared.Schedules, kinds.Schedules);

        static void Check<T>(ImportKindComparison<T> kind, ImportKindChoice? choice) where T : IBackupEntry
        {
            if (choice?.Mode != ImportKindMode.ReplaceAll) return;
            if (kind.HeldBack) throw Photos.VehiclePurgeRefusals.HeldBack(new Photos.VehiclePurge(0, kind.WithPhotos, kind.OnTyreLines));
            if (kind.Backup.Count == 0) throw new DomainException(ReplaceNothingKey, "The backup has none of this kind, so there is nothing to replace yours with.");
        }
    }
}

/// <summary>
/// The work of an import from a backup, inside a run (#337): every chosen car in a savepoint of its own, every entry in one inside it,
/// through the app's own services, so every rule applies and devices follow. The importer owns what comes in, with new ids; sharing,
/// who logged an entry and review marks are not carried. A refused entry (any keyed refusal) follows its car's policy; anything else is
/// unexpected, and the run rolls back whole.
/// <para>
/// By each kind's choice (#338): nothing, only the missing, entry by entry (the ticked new ones added, the ones marked <i>Take the
/// backup's</i> updated in place, checking no version: the owner's decision 7), or <i>Replace all</i> (the car's entries of the kind
/// deleted for good, the trash too, then all of the backup's). A car with a <i>Replace all</i> is all or nothing (decision 1): one
/// refusal puts it back as it was. Then the visits' links, between entries that are both the car's now.
/// </para>
/// </summary>
public sealed class BackupImportWork(
    BackupImportRules rules, VehicleService vehicles, RefuelingService refuelings, ExpenseService expenses, RecurringExpenseService recurring,
    BackupTyreReplay tyreReplay, IExpenseRepository expenseRows, Tyres.ITyreRepository tyreBooks, ILogger<BackupImportWork> logger)
{
    /// <summary>The kinds of a refused tyre row (#339): a set (its name as the title), the lines of no set.</summary>
    public const string TyreSetKind = "tyreSet", NoSetLinesKind = "tyreLines";

    private sealed class CarLeftOut(ImportRefusedEntry refused) : Exception
    {
        public ImportRefusedEntry Refused { get; } = refused;
    }

    public async Task<ImportRunReport> RunAsync(IRun run, Principal starter, IBackupCars file, ImportRunPlan plan, Action<ImportRunProgress> progress, CancellationToken ct)
    {
        var reports = new List<ImportCarReport>();
        var (done, total, carNumber) = (0, plan.Cars.Sum(c => c.Entries), 0);
        foreach (var chosen in plan.Cars)
        {
            carNumber++;
            progress(new ImportRunProgress(carNumber, plan.Cars.Count, done, total));
            await run.ThrowIfStoppedAsync();
            var car = await file.ReadCarAsync(chosen.CarId, ct) ?? throw new InvalidOperationException("A chosen car is no longer in the file.");
            ImportCarReport? report = null;
            var before = done;
            try
            {
                await run.InSavepointAsync(async () => report = await CarAsync(run, starter, file, car, chosen, () => progress(new ImportRunProgress(carNumber, plan.Cars.Count, ++done, total)), ct));
            }
            catch (CarLeftOut left)
            {
                // The car's savepoint is rolled back: nothing of it came in, and the run goes on with the next one.
                done = before + chosen.Entries;
                report = new ImportCarReport(chosen.CarId, car.Car.Name, null, chosen.TargetName, chosen.TargetVehicleId is null, LeftOut: true, 0, 0, 0, 0, 0, 0, [left.Refused], 0,
                    Replacing: chosen.Replacing);
                logger.LogDebug("Run {RunId}: a car was left out after a refused entry ({Key})", run.Id, left.Refused.Key);
            }
            reports.Add(report!);
        }
        progress(new ImportRunProgress(plan.Cars.Count, plan.Cars.Count, total, total));
        return new ImportRunReport(reports);
    }

    private static Dictionary<string, string> ArgsOf(KeyedException e) =>
        e.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? "");

    /// <summary>One save of the car's logs, in the order they are tried: by day, then by reading.</summary>
    private sealed record Step(DateOnly Date, long Order, string Kind, string? Title, Func<Task> Save, Action Saved);

    private async Task<ImportCarReport> CarAsync(IRun run, Principal starter, IBackupCars file, BackupCar car, ImportCarPlan chosen, Action step, CancellationToken ct)
    {
        Vehicle vehicle;
        ImportCarComparison compared;
        ImportTyreComparison tyres = ImportTyreComparison.None;
        var tyreChoice = chosen.Kinds?.Tyres;
        ImportCarWork work;
        var deleted = (Refuelings: 0, Expenses: 0, Schedules: 0);
        try
        {
            if (chosen.TargetVehicleId is { } targetId)
            {
                // Again, now that the run holds the car: what the review saw may have changed since (decisions 7, 8 and 11).
                vehicle = await rules.TargetAsync(starter.Id, car.Car, targetId, ct);
                compared = await rules.CompareAsync(car, vehicle, ct);
                if (tyreChoice is not null) tyres = await rules.CompareTyresAsync(file, car.Car.Id, vehicle, ct);
            }
            else
            {
                vehicle = await vehicles.AddAsync(car.Car.Name, car.Car.LicensePlate, car.Car.FuelType, car.Car.Units, ct);
                compared = BackupImportRules.Whole(car);
                if (tyreChoice is not null) tyres = await rules.CompareTyresAsync(file, car.Car.Id, null, ct);
            }
            // The expenses the chosen tyres' lines hold come with the tyres (#339, the owner's decision 1), not with the expenses.
            work = BackupImportRules.Work(compared, chosen.Kinds, tyres.ExpensesOf(tyreChoice));
            // Replace all: what is there now goes for good, the trash too (decisions 3 and 8), before anything of the backup's is saved.
            // A kind that got a photo or a tyre log line since the review is refused by its purge: the car is put back (decision 11).
            if (work.Refuelings.Purge) deleted.Refuelings = await HeldBackAs(BackupImportRules.Refueling, () => refuelings.PurgeForVehicleAsync(vehicle.Id, ct));
            if (work.Expenses.Purge) deleted.Expenses = await HeldBackAs(BackupImportRules.Expense, () => expenses.PurgeForVehicleAsync(vehicle.Id, ct));
            if (work.Schedules.Purge)
                foreach (var id in compared.Schedules.TargetIds)
                {
                    await recurring.DeleteAsync(id, ct);
                    deleted.Schedules++;
                }
        }
        catch (KeyedException e)
        {
            // The car itself is refused (its target went to the trash or changed its units while the run waited, the new car's own values
            // are refused, or a kind to replace is held back): a refusal, not a failure. Whatever its policy, this car is left out with
            // the reason and the others go on.
            throw new CarLeftOut(new ImportRefusedEntry(ImportRefusedEntry.Car, null, null, e.Key, ArgsOf(e)));
        }

        var refused = new List<ImportRefusedEntry>();
        var refusedMore = 0;
        void Refused(ImportRefusedEntry entry)
        {
            // All or nothing for a car with a Replace all (decision 1); else the car's own choice.
            if (work.Replacing || chosen.Policy == RefusedEntryPolicy.LeaveCarOut) throw new CarLeftOut(entry);
            if (refused.Count < ImportRunReport.MaxRefusedPerCar) refused.Add(entry);
            else refusedMore++;
        }

        int fuelAdded = 0, fuelChanged = 0, expensesAdded = 0, expensesChanged = 0, schedulesAdded = 0, schedulesChanged = 0;
        // The car's entries afterwards, by the backup's id: what was there (the same, or differing) and what is added. For the links.
        var (expenseIds, scheduleIds) = (new Dictionary<Guid, Guid>(work.Expenses.Counterparts), new Dictionary<Guid, Guid>(work.Schedules.Counterparts));

        // The logs: changes and additions together, oldest first, each reading checked against the ones saved before it and what the car
        // has. One that is refused is tried again after the others, as long as a round saves something: a change can need another one
        // first (two readings that both move), and no single order suits every case. What is still refused then is refused.
        var steps = work.Refuelings.Takes.Select(t => new Step(t.Entry.Values.Date, t.Entry.Values.Odometer ?? 0, BackupImportRules.Refueling, null,
                () => refuelings.UpdateAsync(t.TargetId, Whole(t.Entry.Values), ct, recalculateConsumption: false), () => fuelChanged++))
            .Concat(work.Refuelings.Adds.Select(r => new Step(r.Values.Date, r.Values.Odometer ?? 0, BackupImportRules.Refueling, null,
                () => refuelings.LogAsync(vehicle.Id, r.Values, ct, recalculateConsumption: false), () => fuelAdded++)))
            .Concat(work.Expenses.Takes.Select(t => new Step(t.Entry.Values.Date, t.Entry.Values.Odometer ?? 0, BackupImportRules.Expense, t.Entry.Values.Title,
                () => expenses.UpdateAsync(t.TargetId, Whole(t.Entry.Values), ct), () => expensesChanged++)))
            .Concat(work.Expenses.Adds.Select(e => new Step(e.Values.Date, e.Values.Odometer ?? 0, BackupImportRules.Expense, e.Values.Title,
                async () => expenseIds[e.Id] = (await expenses.AddAsync(vehicle.Id, e.Values, ct)).Id, () => expensesAdded++)))
            .OrderBy(s => s.Date).ThenBy(s => s.Order).ToList();
        while (steps.Count > 0)
        {
            var again = new List<(Step Step, ImportRefusedEntry Refusal)>();
            foreach (var entry in steps)
            {
                await run.ThrowIfStoppedAsync();
                try
                {
                    await run.InSavepointAsync(entry.Save);
                    entry.Saved();
                    step();
                }
                catch (KeyedException e)
                {
                    again.Add((entry, new ImportRefusedEntry(entry.Kind, entry.Date, entry.Title, e.Key, ArgsOf(e))));
                }
            }
            if (again.Count == steps.Count)
            {
                foreach (var (_, refusal) in again)
                {
                    step();
                    Refused(refusal);
                }
                break;
            }
            steps = [.. again.Select(a => a.Step)];
        }
        if (fuelAdded + fuelChanged > 0) await refuelings.RecalculateConsumptionAsync(vehicle.Id, ct); // once for the car, not after every entry

        // Schedules last: one that counts distance without a known odometer starts from the car's latest reading, which now includes the import.
        async Task ScheduleAsync(BackupSchedule schedule, Func<Task> save, Action saved)
        {
            await run.ThrowIfStoppedAsync();
            try
            {
                await run.InSavepointAsync(save);
                saved();
            }
            catch (KeyedException e)
            {
                Refused(new ImportRefusedEntry(BackupImportRules.Recurring, null, schedule.Values.Title, e.Key, ArgsOf(e)));
            }
            finally
            {
                step();
            }
        }
        foreach (var (schedule, targetId) in work.Schedules.Takes)
            await ScheduleAsync(schedule, () => recurring.UpdateAsync(targetId, schedule.Values, ct), () => schedulesChanged++);
        foreach (var schedule in work.Schedules.Adds)
            await ScheduleAsync(schedule, async () => scheduleIds[schedule.Id] = (await recurring.AddAsync(vehicle.Id, schedule.Values, ct)).Item.Id, () => schedulesAdded++);

        // The tyres (#339): set by set, each in a savepoint of its own, after the logs (their readings) and the schedules; then the lines
        // of no set. A set the rules refuse is a refused entry like a log's, under the car's policy (all or nothing with a Replace all).
        ImportTyreReport? tyreReport = null;
        if (tyreChoice is not null)
        {
            var heldByLines = (await tyreBooks.LoadAsync(vehicle.Id, ct)).Entries.Where(e => e.ExpenseId is not null).Select(e => e.ExpenseId!.Value).ToHashSet();
            var pool = new ImportExpensePool((await expenseRows.ListAllForVehicleAsync(vehicle.Id, ct))
                .Where(e => !heldByLines.Contains(e.Id)).Select(e => (e.Id, e.Date, e.Title, e.Amount)));
            var (names, lines, singles, noSet, made, amountsHere) = (new List<string>(), 0, 0, 0, 0, 0);
            // A chosen set the car's history refuses now (it changed since the review) is that set's refusal; one here now stays as it is.
            foreach (var (set, offer) in tyres.Sets.Where(s => s.Offer.State == ImportSetState.Refused && tyreChoice.Sets?.Contains(s.Set.Id) == true))
            {
                step();
                Refused(new ImportRefusedEntry(TyreSetKind, null, offer.Name, offer.RefusalKey!, offer.RefusalArgs ?? new Dictionary<string, string>()));
            }
            foreach (var set in tyres.Chosen(tyreChoice))
            {
                await run.ThrowIfStoppedAsync();
                var mark = pool.Mark();
                try
                {
                    TyreReplayed? replayed = null;
                    await run.InSavepointAsync(async () => replayed = await tyreReplay.SetAsync(vehicle.Id, tyres.Backup, set, ct, pool));
                    names.Add(BackupCarShapes.Cut(set.Name)!);
                    (lines, singles, made, amountsHere) = (lines + replayed!.Lines, singles + replayed.Tyres, made + replayed.ExpensesMade, amountsHere + replayed.AmountsHere);
                    foreach (var (old, @new) in replayed.Ids) expenseIds.TryAdd(old, @new); // a tyre line's expense a visit marked done keeps its link
                }
                catch (KeyedException e)
                {
                    pool.Restore(mark);
                    Refused(new ImportRefusedEntry(TyreSetKind, null, BackupCarShapes.Cut(set.Name), e.Key, ArgsOf(e)));
                }
                finally
                {
                    step();
                }
            }
            if (tyreChoice.NoSetLines && tyres.NoSetLines.Count > 0)
            {
                await run.ThrowIfStoppedAsync();
                var mark = pool.Mark();
                try
                {
                    TyreReplayed? replayed = null;
                    await run.InSavepointAsync(async () => replayed = await tyreReplay.LinesOfNoSetAsync(vehicle.Id, tyres.Backup, [.. tyres.NoSetLines.Select(l => l.Id)], ct, pool));
                    (noSet, made, amountsHere) = (replayed!.Lines, made + replayed.ExpensesMade, amountsHere + replayed.AmountsHere);
                    foreach (var (old, @new) in replayed.Ids) expenseIds.TryAdd(old, @new);
                }
                catch (KeyedException e)
                {
                    pool.Restore(mark);
                    Refused(new ImportRefusedEntry(NoSetLinesKind, null, null, e.Key, ArgsOf(e)));
                }
                finally
                {
                    step();
                }
            }
            tyreReport = new ImportTyreReport(names, lines, singles, noSet, made, amountsHere);
        }

        // The visits' links (#338): an expense of the backup and a schedule it marked done, when both are the car's now and neither kind
        // was left out. One that is there already stays as it is: a second run adds nothing.
        var links = 0;
        if (work.Links)
            foreach (var expense in car.Expenses)
                foreach (var schedule in expense.CompletedScheduleIds)
                    if (expenseIds.TryGetValue(expense.Id, out var expenseId) && scheduleIds.TryGetValue(schedule, out var scheduleId)
                        && await recurring.LinkAsync(expenseId, scheduleId, ct)) links++;

        var left = Leftovers(compared, work);
        return new ImportCarReport(
            chosen.CarId, car.Car.Name, vehicle.Id, chosen.TargetVehicleId is null ? null : vehicle.Name, chosen.TargetVehicleId is null, LeftOut: false,
            fuelAdded, expensesAdded, schedulesAdded,
            left.Same, left.Differ, left.InTrash, refused, refusedMore,
            [
                new ImportKindReport(BackupImportRules.Refueling, work.Refuelings.Mode, fuelAdded, fuelChanged, deleted.Refuelings),
                new ImportKindReport(BackupImportRules.Expense, work.Expenses.Mode, expensesAdded, expensesChanged, deleted.Expenses),
                new ImportKindReport(BackupImportRules.Recurring, work.Schedules.Mode, schedulesAdded, schedulesChanged, deleted.Schedules),
            ], work.Replacing, links, tyreReport);
    }

    /// <summary>What the car's choices leave as it is, all kinds together.</summary>
    internal static (int Same, int Differ, int InTrash) Leftovers(ImportCarComparison compared, ImportCarWork work)
    {
        var (f, e, s) = (BackupImportRules.Left(compared.Refuelings, work.Refuelings), BackupImportRules.Left(compared.Expenses, work.Expenses), BackupImportRules.Left(compared.Schedules, work.Schedules));
        return (f.Same + e.Same + s.Same, f.Differ + e.Differ + s.Differ, f.InTrash + e.InTrash);
    }

    /// <summary>A purge's refusal, with the kind it is about (the report's reason names it).</summary>
    private static async Task<int> HeldBackAs(string kind, Func<Task<int>> purge)
    {
        try
        {
            return await purge();
        }
        catch (DomainException e) when (e.Key == Photos.VehiclePurgeRefusals.HeldBackKey)
        {
            throw new DomainException(e.Key, e.Message, new { Kind = kind, Photos = e.Args["photos"], Lines = e.Args["lines"] });
        }
    }

    /// <summary>
    /// The backup's values as an update takes them: a discount the backup has none of is sent as none (left out, the entry's own would
    /// stay, and without the backup's amount it would be refused as a discount with nothing to take it off).
    /// </summary>
    private static RefuelingInput Whole(RefuelingInput values) => values with { Discount = values.Discount ?? 0 };

    private static ExpenseInput Whole(ExpenseInput values) => values with { Discount = values.Discount ?? 0 };
}
