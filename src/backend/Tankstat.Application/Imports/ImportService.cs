using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain;
using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

public sealed record ImportUpload(string Token, string Format);

/// <param name="DuplicateFuelRows">Rows that match an existing log of the target vehicle (same date and odometer); 0 for a new vehicle.</param>
/// <param name="DuplicateExpenseRows">Rows that match an existing expense (same date, title and amount).</param>
/// <param name="DuplicateRecurringRows">Recurring expenses whose title an existing recurring expense of the target vehicle already has.</param>
public sealed record ImportPreview(
    ImportedVehicle? SourceVehicle, int FuelRows, int ExpenseRows, int RecurringRows, int DuplicateFuelRows, int DuplicateExpenseRows, int DuplicateRecurringRows,
    DateOnly? FirstDate, DateOnly? LastDate, IReadOnlyList<string> Categories, IReadOnlyList<ImportIssue> Issues);

public sealed record NewVehicleSpec(string Name, string? LicensePlate, FuelType FuelType, MeasurementUnits Units);

/// <summary>Where the rows go: an existing vehicle (the user needs Edit access to its logs) or a new one.</summary>
public sealed record ImportTarget(Guid? VehicleId, NewVehicleSpec? NewVehicle);

/// <param name="Currency">The currency of every amount in the file (files do not carry one); omit for the instance default.</param>
/// <param name="ImportDuplicates">When false, rows that match an existing record are skipped.</param>
public sealed record ImportOptions(string? Currency, bool ImportDuplicates);

public sealed record ImportResult(
    Guid VehicleId, int FuelImported, int ExpensesImported, int RecurringImported,
    int FuelSkippedDuplicates, int ExpensesSkippedDuplicates, int RecurringSkippedDuplicates, IReadOnlyList<ImportIssue> Errors);

/// <summary>
/// Importing logs from another app, in two steps so nothing is saved unseen: <see cref="UploadAsync"/> parses a file with the parser of its
/// format and keeps the result under a token; <see cref="PreviewAsync"/> shows what would happen for a target vehicle; and
/// <see cref="CommitAsync"/> saves it. Rows are saved through the same services as rows typed in by hand, so every rule (access, odometer
/// order, dates, amounts) applies; a row that breaks a rule is reported and the others are still imported.
/// </summary>
public sealed class ImportService(
    IEnumerable<IImportParser> parsers, ImportSessionStore sessions, AccessService access,
    VehicleService vehicles, RefuelingService refuelings, ExpenseService expenses, RecurringExpenseService recurring,
    IRefuelingRepository refuelingRepository, IExpenseRepository expenseRepository, IOptions<VehicleDefaultsOptions> defaults)
{
    public IReadOnlyList<string> Formats => parsers.Select(p => p.Format).ToList();

    public async Task<ImportUpload> UploadAsync(string format, Stream content, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var parser = parsers.FirstOrDefault(p => p.Format.Equals(format, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException("import.unknownFormat", $"Unknown import format '{format}'.", new { Format = format });
        var batch = parser.Parse(content);
        if (batch.FuelLogs.Count == 0 && batch.Expenses.Count == 0 && batch.Recurring.Count == 0)
            throw new DomainException("import.nothingFound", "The file contains no fuel logs, expenses or recurring expenses to import.");
        return new ImportUpload(sessions.Save(user.Id, batch), parser.Format);
    }

    public async Task<ImportPreview> PreviewAsync(string token, Guid? vehicleId, CancellationToken ct)
    {
        var batch = await BatchAsync(token, ct);
        var issues = batch.Issues.ToList();
        int duplicateFuel = 0, duplicateExpenses = 0, duplicateRecurring = 0;

        if (vehicleId is { } id)
        {
            var vehicle = await EditableVehicleAsync(id, ct);
            var duplicates = await DuplicatesAsync(batch, id, ct);
            (duplicateFuel, duplicateExpenses, duplicateRecurring) = (duplicates.Fuel.Count, duplicates.Expenses.Count, duplicates.Recurring.Count);
            var file = batch.Vehicle;
            if ((file?.Distance is { } distance && distance != vehicle.Units.Distance) || (file?.Volume is { } volume && volume != vehicle.Units.Volume))
                issues.Add(new ImportIssue("vehicle", 0, "import.unitsDiffer", new Dictionary<string, object?>()));
        }

        var dates = batch.FuelLogs.Select(l => l.Date).Concat(batch.Expenses.Select(e => e.Date)).ToList();
        return new ImportPreview(
            batch.Vehicle, batch.FuelLogs.Count, batch.Expenses.Count, batch.Recurring.Count, duplicateFuel, duplicateExpenses, duplicateRecurring,
            dates.Count == 0 ? null : dates.Min(), dates.Count == 0 ? null : dates.Max(),
            batch.Expenses.Select(e => e.Category).Concat(batch.Recurring.Select(r => r.Category)).Where(c => c is not null).Select(c => c!).Distinct().Order().ToList(), issues);
    }

    public async Task<ImportResult> CommitAsync(string token, ImportTarget target, ImportOptions options, CancellationToken ct)
    {
        var batch = await BatchAsync(token, ct);
        var currency = CurrencyCode.Normalize(options.Currency ?? defaults.Value.Currency);

        Guid vehicleId;
        if (target is { VehicleId: { } existing })
        {
            vehicleId = (await EditableVehicleAsync(existing, ct)).Id;
        }
        else if (target.NewVehicle is { } spec)
        {
            vehicleId = (await vehicles.AddAsync(spec.Name, spec.LicensePlate, spec.FuelType, spec.Units, ct)).Id;
        }
        else throw new DomainException("import.targetRequired", "Choose a vehicle or create a new one.");

        var (skippedFuel, skippedExpenses, skippedRecurring) = (new HashSet<ImportedFuelLog>(), new HashSet<ImportedExpense>(), new HashSet<ImportedRecurring>());
        if (target.VehicleId is not null && !options.ImportDuplicates)
        {
            var duplicates = await DuplicatesAsync(batch, vehicleId, ct);
            skippedFuel = duplicates.Fuel;
            skippedExpenses = duplicates.Expenses;
            skippedRecurring = duplicates.Recurring;
        }

        // Oldest first: each row's odometer is checked against the rows saved before it (and what the vehicle already had).
        var rows = batch.FuelLogs.Where(l => !skippedFuel.Contains(l)).Select(l => (l.Date, Order: l.Odometer, Fuel: l, Expense: (ImportedExpense?)null))
            .Concat(batch.Expenses.Where(e => !skippedExpenses.Contains(e)).Select(e => (e.Date, Order: e.Odometer ?? 0, Fuel: (ImportedFuelLog?)null, Expense: (ImportedExpense?)e)))
            .OrderBy(r => r.Date).ThenBy(r => r.Order).ToList();

        var errors = new List<ImportIssue>();
        int fuelDone = 0, expensesDone = 0;
        foreach (var row in rows)
        {
            try
            {
                if (row.Fuel is { } f)
                {
                    await refuelings.LogAsync(vehicleId, new RefuelingInput(f.Date, f.Volume, f.TotalCost, currency, f.Odometer, f.IsFullTank, f.Note), ct, recalculateConsumption: false);
                    fuelDone++;
                }
                else if (row.Expense is { } e)
                {
                    await expenses.AddAsync(vehicleId, new ExpenseInput(e.Date, e.Title, e.Category, e.Amount, currency, e.Odometer, e.Note), ct);
                    expensesDone++;
                }
            }
            catch (DomainException ex)
            {
                var (section, n) = row.Fuel is { } fl ? ("log", fl.SourceRow) : ("costs", row.Expense!.SourceRow);
                errors.Add(new ImportIssue(section, n, ex.Key, ex.Args));
            }
        }

        if (fuelDone > 0) await refuelings.RecalculateConsumptionAsync(vehicleId, ct); // once for the whole import

        // Schedules last: one that counts distance without a known odometer starts from the vehicle's latest reading, which now includes the import.
        var recurringDone = 0;
        foreach (var r in batch.Recurring.Where(r => !skippedRecurring.Contains(r)))
        {
            try
            {
                await recurring.AddAsync(vehicleId, new RecurringExpenseInput(r.Title, r.Category, r.Note, r.Kind, r.IntervalMonths, r.IntervalDistance, r.LastDoneDate, r.LastDoneOdometer, null, null), ct);
                recurringDone++;
            }
            catch (DomainException ex)
            {
                errors.Add(new ImportIssue("costs", r.SourceRow, ex.Key, ex.Args));
            }
        }

        sessions.Remove(token);
        return new ImportResult(vehicleId, fuelDone, expensesDone, recurringDone, skippedFuel.Count, skippedExpenses.Count, skippedRecurring.Count, errors);
    }

    private async Task<ImportBatch> BatchAsync(string token, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        return sessions.Find(user.Id, token) ?? throw new NotFoundException("import.expired", "This import has expired; upload the file again.");
    }

    /// <summary>A vehicle the user may add logs to; others look missing, view-only users are forbidden.</summary>
    private async Task<Vehicle> EditableVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var vehicle = await vehicles.FindAsync(vehicleId, ct);
        var level = await refuelings.LevelForVehicleAsync(vehicleId, ct);
        if (vehicle is null || level < AccessLevel.View) throw new NotFoundException("vehicle.notFound", $"Vehicle {vehicleId} does not exist.", new { Id = vehicleId });
        if (level < AccessLevel.Edit) throw new ForbiddenException("vehicle.viewOnly", "You may only view this vehicle.");
        return vehicle;
    }

    /// <summary>
    /// Rows that match something the vehicle already has: a fill-up on the same date with the same odometer, an expense with the same date,
    /// title and amount, a recurring expense with the same title.
    /// </summary>
    private async Task<(HashSet<ImportedFuelLog> Fuel, HashSet<ImportedExpense> Expenses, HashSet<ImportedRecurring> Recurring)> DuplicatesAsync(ImportBatch batch, Guid vehicleId, CancellationToken ct)
    {
        var existingFuel = (await refuelingRepository.ListAllForVehicleAsync(vehicleId, ct)).Select(r => (r.Date, r.Odometer)).ToHashSet();
        var existingExpenses = (await expenseRepository.ListAllForVehicleAsync(vehicleId, ct)).Select(e => (e.Date, e.Title.ToLowerInvariant(), e.Amount)).ToHashSet();
        var existingRecurring = (await recurring.ListAsync(vehicleId, ct)).Select(r => r.Item.Title.ToLowerInvariant()).ToHashSet();
        return (
            batch.FuelLogs.Where(l => existingFuel.Contains((l.Date, l.Odometer))).ToHashSet(),
            batch.Expenses.Where(e => existingExpenses.Contains((e.Date, e.Title.ToLowerInvariant(), e.Amount))).ToHashSet(),
            batch.Recurring.Where(r => existingRecurring.Contains(r.Title.ToLowerInvariant())).ToHashSet());
    }
}
