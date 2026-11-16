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

/// <summary>What of a backup's car is to come into a target car, and what is not, worked out against the target as it is now.</summary>
public sealed record ImportCarDelta(
    IReadOnlyList<BackupRefueling> Refuelings, IReadOnlyList<BackupExpense> Expenses, IReadOnlyList<BackupSchedule> Schedules,
    int ExistingRefuelings, int ExistingExpenses, int ExistingSchedules, int Differing, int InTrash)
{
    public int Entries => Refuelings.Count + Expenses.Count + Schedules.Count;
    public int Existing => ExistingRefuelings + ExistingExpenses + ExistingSchedules;
}

/// <summary>
/// The rules of an import from a backup that the review and the run share (#337), so the run does what the review said, as far as the
/// cars stayed as they were: which cars may be targets, and what of a car is missing from its target.
/// </summary>
public sealed class BackupImportRules(IImportTargetReader targets, IVehicleRepository vehicles)
{
    public const string UnitsDifferKey = "import.unitsDiffer";

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
        if (choices.Any(c => file.Cars.All(f => f.Id != c.CarId))) throw new DomainException("import.unknownCar", "The file has no such car.");
        if (choices.GroupBy(c => c.CarId).Any(g => g.Count() > 1) || choices.Where(c => c.TargetVehicleId is not null).GroupBy(c => c.TargetVehicleId).Any(g => g.Count() > 1))
            throw new DomainException("import.carTwice", "A car can be chosen once, and each of your cars can take one car of the file.");
    }

    /// <summary>
    /// Only the missing: what <see cref="BackupCarComparer"/> calls only in the backup, without what is in the target's trash (the
    /// owner's decision 1: said and left; by id, else by key, like the live ones). What differs stays as it is.
    /// </summary>
    public async Task<ImportCarDelta> MissingAsync(BackupCar car, Vehicle target, CancellationToken ct)
    {
        var compared = BackupCarComparer.Compare(car, await targets.LiveAsync(target, ct));
        var missing = car with
        {
            Refuelings = [.. compared.Refuelings.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            Expenses = [.. compared.Expenses.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            Schedules = [],
        };
        var trashed = BackupCarComparer.Compare(missing, await targets.TrashAsync(target, ct));
        return new ImportCarDelta(
            [.. trashed.Refuelings.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            [.. trashed.Expenses.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            [.. compared.Schedules.Entries.Where(e => e.Match == BackupEntryMatch.OnlyInBackup).Select(e => e.Entry)],
            compared.Refuelings.Same, compared.Expenses.Same, compared.Schedules.Same,
            compared.Refuelings.Differs + compared.Expenses.Differs + compared.Schedules.Differs,
            missing.Refuelings.Count + missing.Expenses.Count - trashed.Refuelings.OnlyInBackup - trashed.Expenses.OnlyInBackup);
    }

    /// <summary>A car that comes as a new one brings everything.</summary>
    public static ImportCarDelta Everything(BackupCar car) => new(car.Refuelings, car.Expenses, car.Schedules, 0, 0, 0, 0, 0);
}

/// <summary>
/// The work of an import from a backup, inside a run (#337): every chosen car in a savepoint of its own, every entry in one inside it,
/// through the app's own services, so every rule applies and devices follow. The importer owns what comes in, with new ids; sharing,
/// who logged an entry and review marks are not carried. A refused entry (any keyed refusal) follows its car's policy; anything else is
/// unexpected, and the run rolls back whole. A visit's link to its schedules is not carried here (#338).
/// </summary>
public sealed class BackupImportWork(
    BackupImportRules rules, VehicleService vehicles, RefuelingService refuelings, ExpenseService expenses, RecurringExpenseService recurring,
    ILogger<BackupImportWork> logger)
{
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
                await run.InSavepointAsync(async () => report = await CarAsync(run, starter, car, chosen, () => progress(new ImportRunProgress(carNumber, plan.Cars.Count, ++done, total)), ct));
            }
            catch (CarLeftOut left)
            {
                // The car's savepoint is rolled back: nothing of it came in, and the run goes on with the next one.
                done = before + chosen.Entries;
                report = new ImportCarReport(chosen.CarId, car.Car.Name, null, chosen.TargetName, chosen.TargetVehicleId is null, LeftOut: true, 0, 0, 0, 0, 0, 0, [left.Refused], 0);
                logger.LogDebug("Run {RunId}: a car was left out after a refused entry ({Key})", run.Id, left.Refused.Key);
            }
            reports.Add(report!);
        }
        progress(new ImportRunProgress(plan.Cars.Count, plan.Cars.Count, total, total));
        return new ImportRunReport(reports);
    }

    private async Task<ImportCarReport> CarAsync(IRun run, Principal starter, BackupCar car, ImportCarPlan chosen, Action step, CancellationToken ct)
    {
        Vehicle vehicle;
        ImportCarDelta delta;
        if (chosen.TargetVehicleId is { } targetId)
        {
            // Again, now that the run holds the car: what the review saw may have changed since.
            vehicle = await rules.TargetAsync(starter.Id, car.Car, targetId, ct);
            delta = await rules.MissingAsync(car, vehicle, ct);
        }
        else
        {
            vehicle = await vehicles.AddAsync(car.Car.Name, car.Car.LicensePlate, car.Car.FuelType, car.Car.Units, ct);
            delta = BackupImportRules.Everything(car);
        }

        var refused = new List<ImportRefusedEntry>();
        var refusedMore = 0;
        async Task<bool> EntryAsync(Func<Task> save, string kind, DateOnly? date, string? title)
        {
            await run.ThrowIfStoppedAsync();
            try
            {
                await run.InSavepointAsync(save);
                return true;
            }
            catch (KeyedException e)
            {
                var entry = new ImportRefusedEntry(kind, date, title, e.Key, e.Args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? ""));
                if (chosen.Policy == RefusedEntryPolicy.LeaveCarOut) throw new CarLeftOut(entry);
                if (refused.Count < ImportRunReport.MaxRefusedPerCar) refused.Add(entry);
                else refusedMore++;
                return false;
            }
            finally
            {
                step();
            }
        }

        // Oldest first: each reading is checked against the ones saved before it and what the car already had.
        var rows = delta.Refuelings.Select(r => (r.Values.Date, Order: r.Values.Odometer ?? 0, Fuel: (BackupRefueling?)r, Expense: (BackupExpense?)null))
            .Concat(delta.Expenses.Select(e => (e.Values.Date, Order: e.Values.Odometer ?? 0, Fuel: (BackupRefueling?)null, Expense: (BackupExpense?)e)))
            .OrderBy(r => r.Date).ThenBy(r => r.Order).ToList();
        int fuelDone = 0, expensesDone = 0, schedulesDone = 0;
        foreach (var row in rows)
        {
            if (row.Fuel is { } fuel)
            {
                if (await EntryAsync(() => refuelings.LogAsync(vehicle.Id, fuel.Values, ct, recalculateConsumption: false), "refueling", fuel.Values.Date, null)) fuelDone++;
            }
            else if (row.Expense is { } expense && await EntryAsync(() => expenses.AddAsync(vehicle.Id, expense.Values, ct), "expense", expense.Values.Date, expense.Values.Title)) expensesDone++;
        }
        if (fuelDone > 0) await refuelings.RecalculateConsumptionAsync(vehicle.Id, ct); // once for the car

        // Schedules last: one that counts distance without a known odometer starts from the car's latest reading, which now includes the import.
        foreach (var schedule in delta.Schedules)
            if (await EntryAsync(() => recurring.AddAsync(vehicle.Id, schedule.Values, ct), "recurring", null, schedule.Values.Title)) schedulesDone++;

        return new ImportCarReport(
            chosen.CarId, car.Car.Name, vehicle.Id, chosen.TargetVehicleId is null ? null : vehicle.Name, chosen.TargetVehicleId is null, LeftOut: false,
            fuelDone, expensesDone, schedulesDone, delta.Existing, delta.Differing, delta.InTrash, refused, refusedMore);
    }
}
