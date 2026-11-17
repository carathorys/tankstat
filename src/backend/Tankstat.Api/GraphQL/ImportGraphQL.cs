using System.Text.Json;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Imports;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Api.GraphQL;

/// <summary>A skipped row or a problem: a stable <c>key</c> and its <c>args</c> (translated by the client), and where in the file it was.</summary>
public sealed record ImportIssueInfo(string Section, int Row, string Key, JsonElement Args)
{
    public static ImportIssueInfo From(ImportIssue issue) => new(issue.Section, issue.Row, issue.Key, JsonSerializer.SerializeToElement(issue.Args));
}

/// <summary>What the file says about its vehicle (any part may be unknown).</summary>
public sealed record ImportedVehicleInfo(string? Name, string? LicensePlate, FuelType? FuelType, DistanceUnit? DistanceUnit, VolumeUnit? VolumeUnit);

public sealed record ImportPreviewInfo(
    ImportedVehicleInfo? SourceVehicle, int FuelRows, int ExpenseRows, int RecurringRows, int DuplicateFuelRows, int DuplicateExpenseRows, int DuplicateRecurringRows,
    DateOnly? FirstDate, DateOnly? LastDate, IReadOnlyList<string> Categories, IReadOnlyList<ImportIssueInfo> Issues);

public sealed record ImportResultInfo(
    Guid VehicleId, int FuelImported, int ExpensesImported, int RecurringImported,
    int FuelSkippedDuplicates, int ExpensesSkippedDuplicates, int RecurringSkippedDuplicates, IReadOnlyList<ImportIssueInfo> Errors);

public sealed record NewVehicleInput(string Name, string? LicensePlate, FuelType FuelType, MeasurementUnitsInput Units);

/// <param name="Token">From the upload (<c>POST /imports/{format}</c>).</param>
/// <param name="VehicleId">The vehicle to add the rows to, or omit it and give <c>newVehicle</c>.</param>
/// <param name="Currency">The currency of every amount in the file; omit for the instance default.</param>
public sealed record ConfirmImportInput(string Token, Guid? VehicleId, NewVehicleInput? NewVehicle, string? Currency, bool ImportDuplicates);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ImportQueries
{
    /// <summary>What importing an uploaded file would do for a vehicle (duplicates are only known for an existing vehicle). Nothing is saved.</summary>
    public async Task<ImportPreviewInfo> GetImportPreview(string token, Guid? vehicleId, [Service] ImportService imports, CancellationToken ct)
    {
        var p = await imports.PreviewAsync(token, vehicleId, ct);
        return new ImportPreviewInfo(
            p.SourceVehicle is { } v ? new ImportedVehicleInfo(v.Name, v.LicensePlate, v.FuelType, v.Distance, v.Volume) : null,
            p.FuelRows, p.ExpenseRows, p.RecurringRows, p.DuplicateFuelRows, p.DuplicateExpenseRows, p.DuplicateRecurringRows, p.FirstDate, p.LastDate, p.Categories,
            p.Issues.Select(ImportIssueInfo.From).ToList());
    }

    /// <summary>The file formats that can be imported (<c>fuelio</c>, ...).</summary>
    public IReadOnlyList<string> GetImportFormats([Service] ImportService imports) => imports.Formats;
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class ImportMutations
{
    /// <summary>Saves an uploaded file's rows. Rows that break a rule are reported in <c>errors</c>; the others are still imported.</summary>
    public async Task<ImportResultInfo> ConfirmImport(ConfirmImportInput input, [Service] ImportService imports, CancellationToken ct)
    {
        var target = new ImportTarget(input.VehicleId,
            input.NewVehicle is { } n ? new NewVehicleSpec(n.Name, n.LicensePlate, n.FuelType, n.Units.ToDomain()) : null);
        var r = await imports.CommitAsync(input.Token, target, new ImportOptions(input.Currency, input.ImportDuplicates), ct);
        return new ImportResultInfo(r.VehicleId, r.FuelImported, r.ExpensesImported, r.RecurringImported,
            r.FuelSkippedDuplicates, r.ExpensesSkippedDuplicates, r.RecurringSkippedDuplicates, r.Errors.Select(ImportIssueInfo.From).ToList());
    }
}

/// <summary>A car in a backup or an export being imported from (#336): what it is, whose it was by name, and how much it holds.</summary>
public sealed record BackupCarInfo(
    Guid Id, string Name, string? LicensePlate, FuelType FuelType, DistanceUnit DistanceUnit, VolumeUnit VolumeUnit, string? OwnerName, int Refuelings, int Expenses, int Schedules)
{
    public static BackupCarInfo From(BackupCarSummary c) =>
        new(c.Id, c.Name, c.LicensePlate, c.FuelType, c.Units.Distance, c.Units.Volume, c.OwnerName, c.Refuelings, c.Expenses, c.Schedules);
}

/// <summary>What an opened file holds. <c>version</c> is the file's own word about the release that wrote it: text to show, nothing more.</summary>
public sealed record BackupFileInfo(DateTimeOffset CreatedAt, string Version, bool IsExport, IReadOnlyList<BackupCarInfo> Cars, int TrashedCars);

/// <summary>Why a file was refused: a stable <c>key</c> and its <c>args</c>, translated by the client.</summary>
public sealed record BackupImportRefusalInfo(string Key, JsonElement Args);

/// <summary>
/// A person's uploaded backup or export: waiting for its passphrase (<c>triesLeft</c> of three; <c>wrongKey</c> after a wrong one), being
/// opened (ask again: a large file takes a while), opened (<c>file</c>), or ended (<c>refusal</c> says why; the file is gone from the server).
/// </summary>
public sealed record BackupImportInfo(string Token, BackupImportState State, int? TriesLeft, bool WrongKey, BackupFileInfo? File, BackupImportRefusalInfo? Refusal)
{
    public static BackupImportInfo From(BackupImportStatus s) => new(
        s.Token, s.State, s.TriesLeft, s.WrongKey,
        s.Summary is { } f ? new BackupFileInfo(f.CreatedAt, f.Version, f.IsExport, [.. f.Cars.Select(BackupCarInfo.From)], f.TrashedCars) : null,
        s.Refusal is { } r ? new BackupImportRefusalInfo(r.Key, JsonSerializer.SerializeToElement(r.Args)) : null);
}

/// <param name="Token">From the upload (<c>POST /imports/tankstat</c>).</param>
/// <param name="Passphrase">The passphrase the backup's key was made from. It is used for this try and kept nowhere.</param>
public sealed record OpenBackupImportInput(string Token, string? Passphrase);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class BackupImportQueries
{
    /// <summary>How the uploaded file stands. Asked again while it is being opened.</summary>
    public async Task<BackupImportInfo> GetBackupImport(string token, [Service] BackupImportService imports, CancellationToken ct) =>
        BackupImportInfo.From(await imports.StatusAsync(token, ct));

    /// <summary>Whether the signed-in person may import cars from a backup or an export (<c>Imports:BackupEnabled</c>; administrators always may).</summary>
    public Task<bool> GetCanImportFromBackup([Service] BackupImportService imports, CancellationToken ct) => imports.MayImportAsync(ct);
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class BackupImportMutations
{
    /// <summary>Tries the passphrase on an encrypted file: one try at a time, three in all, then the file is removed from the server.</summary>
    public async Task<BackupImportInfo> OpenBackupImport(OpenBackupImportInput input, [Service] BackupImportService imports, CancellationToken ct) =>
        BackupImportInfo.From(await imports.SubmitPassphraseAsync(input.Token, input.Passphrase, ct));

    /// <summary>Gives the uploaded file up: it is deleted from the server now.</summary>
    public async Task<bool> EndBackupImport(string token, [Service] BackupImportService imports, CancellationToken ct)
    {
        await imports.EndAsync(token, ct);
        return true;
    }
}

/// <summary>A car of the importer's own that a backup's car can be added to.</summary>
public sealed record ImportOwnCarInfo(Guid Id, string Name, string? LicensePlate, DistanceUnit DistanceUnit, VolumeUnit VolumeUnit);

/// <summary>A car the file offers: <c>ownedByMe</c> when the backup's owner of it is the importer; <c>suggestedTargetId</c> the app's match among the importer's own cars.</summary>
public sealed record ImportCarOfferInfo(BackupCarInfo Car, bool OwnedByMe, Guid? SuggestedTargetId);

public sealed record ImportCarOffersInfo(BackupFileInfo File, IReadOnlyList<ImportCarOfferInfo> Cars, IReadOnlyList<ImportOwnCarInfo> Targets);

/// <summary>
/// What comes of one kind of a car's entries. With <c>CHOOSE</c> the picks are a default and its exceptions (the backup's entry ids):
/// <c>addNew</c> (true when omitted) for the entries only in the backup, <c>takeBackups</c> (false when omitted) for the ones that differ.
/// </summary>
public sealed record ImportKindChoiceInput(ImportKindMode Mode, bool? AddNew, IReadOnlyList<Guid>? NewExceptions, bool? TakeBackups, IReadOnlyList<Guid>? DifferExceptions)
{
    public ImportKindChoice ToChoice() => new(Mode, AddNew ?? true, NewExceptions, TakeBackups ?? false, DifferExceptions);
}

/// <param name="TargetVehicleId">One of the importer's own cars to add the car to; omit to import it as a new car.</param>
/// <param name="Policy">What happens when one of its entries cannot be saved; omit for the rest to be imported. A car with a <c>REPLACE_ALL</c> is always all or nothing.</param>
/// <param name="Refuelings">What comes of its refuellings; omit for only the missing. A new car's kind comes whole unless it is <c>DONT_IMPORT</c>.</param>
/// <param name="ConfirmedName">The target car's name as the person typed it: a start with a <c>REPLACE_ALL</c> is refused without it.</param>
public sealed record ImportCarChoiceInput(
    Guid CarId, Guid? TargetVehicleId, RefusedEntryPolicy? Policy, ImportKindChoiceInput? Refuelings = null, ImportKindChoiceInput? Expenses = null,
    ImportKindChoiceInput? Schedules = null, string? ConfirmedName = null)
{
    public ImportCarChoice ToChoice() => new(CarId, TargetVehicleId, Policy ?? RefusedEntryPolicy.ImportTheRest,
        Refuelings is null && Expenses is null && Schedules is null ? null : new ImportCarKinds(Refuelings?.ToChoice(), Expenses?.ToChoice(), Schedules?.ToChoice()),
        ConfirmedName);
}

/// <summary>
/// One kind (<c>refueling</c>, <c>expense</c>, <c>recurring</c>) of a chosen car. Of the backup's entries: <c>new</c> only there,
/// <c>differ</c>, <c>same</c>, <c>inTrash</c> matching the target's trash. By the choice: <c>adds</c> will be added and <c>takes</c> of the
/// target's take the backup's values. The target has <c>targetLive</c> and <c>targetTrashed</c> of the kind; <c>withPhotos</c> and
/// <c>onTyreLines</c> of them hold a Replace all back.
/// </summary>
public sealed record ImportKindReviewInfo(
    string Kind, ImportKindMode Mode, int Backup, int New, int Differ, int Same, int InTrash, int Adds, int Takes, int TargetLive, int TargetTrashed, int WithPhotos, int OnTyreLines);

/// <param name="Links">Visits keep their link between an expense and the recurring expenses it marked done (neither kind is left out).</param>
/// <param name="LinksLost">The target's such links that will not be there after the import.</param>
public sealed record ImportCarReviewInfo(
    Guid CarId, string Name, string? OwnerName, bool OwnedByMe, Guid? TargetVehicleId, string? TargetName, int NewRefuelings, int NewExpenses, int NewSchedules,
    int ExistingRefuelings, int ExistingExpenses, int ExistingSchedules, int Differing, int InTrash, IReadOnlyList<ImportKindReviewInfo> Kinds, bool Links, int LinksLost);

/// <summary>A value that differs between the backup's entry and the car's, by the input's field name; each side as invariant text (a day as yyyy-MM-dd, an enum by its name here, such as TIME).</summary>
public sealed record ImportFieldDifferenceInfo(string Field, string? Backup, string? Target);

/// <summary>An entry of the backup in the Choose dialog; for a recurring expense <c>date</c> and <c>odometer</c> are when it was last done.</summary>
public sealed record ImportEntryInfo(Guid Id, DateOnly? Date, long? Odometer, string? Title, decimal? Amount, string? Currency, decimal? Volume, IReadOnlyList<ImportFieldDifferenceInfo> Differences);

public sealed record ImportEntryPageInfo(int Total, IReadOnlyList<ImportEntryInfo> Items);

/// <param name="Token">From the upload (<c>POST /imports/tankstat</c>), once the file is opened.</param>
public sealed record StartBackupImportInput(string Token, IReadOnlyList<ImportCarChoiceInput> Cars);

public sealed record ImportRunProgressInfo(int Car, int Cars, int Done, int Total);

public sealed record ImportRunCarInfo(Guid CarId, string Name, Guid? TargetVehicleId, string? TargetName);

/// <summary>An entry that could not be saved: its kind (<c>refueling</c>, <c>expense</c>, <c>recurring</c>), its day or title, and the reason's key and arguments.</summary>
public sealed record ImportRefusedEntryInfo(string Kind, DateOnly? Date, string? Title, string Key, JsonElement Args);

/// <summary>What came of one kind of a car: added from the backup, changed to the backup's values, and deleted for good first.</summary>
public sealed record ImportKindReportInfo(string Kind, ImportKindMode Mode, int Imported, int Changed, int Deleted);

/// <param name="Replacing">The car had a Replace all: with <c>leftOut</c> it was left exactly as it was.</param>
/// <param name="Links">The visits' links that were added.</param>
public sealed record ImportCarReportInfo(
    Guid CarId, string Name, Guid? VehicleId, string? TargetName, bool AsNew, bool LeftOut, int Refuelings, int Expenses, int Schedules, int Existing, int Differing, int InTrash,
    IReadOnlyList<ImportRefusedEntryInfo> Refused, int RefusedMore, IReadOnlyList<ImportKindReportInfo> Kinds, bool Replacing, int Links);

/// <summary>
/// A run of an import: waiting in line (<c>ahead</c> runs before it), running (<c>progress</c>), done (<c>report</c>, its starter's alone)
/// or not finished (<c>failure</c>; <c>fileToken</c> when the file is still open, to go back to the review).
/// </summary>
public sealed record ImportRunInfo(
    Guid Id, Tankstat.Domain.Imports.ImportRunKind Kind, string? StartedByName, bool Mine, Tankstat.Domain.Imports.ImportRunState State, Tankstat.Domain.Imports.ImportRunFailure? Failure,
    DateTimeOffset QueuedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, int Ahead, ImportRunProgressInfo Progress, IReadOnlyList<ImportRunCarInfo> Cars,
    IReadOnlyList<ImportCarReportInfo>? Report, string? FileToken, int LimitMinutes)
{
    public static ImportRunInfo From(ImportRunView v, Guid viewer, string? fileToken, int limitMinutes) => new(
        v.Id, v.Kind, v.StartedByName, v.StartedById == viewer, v.State, v.Failure, v.QueuedAt, v.StartedAt, v.EndedAt, v.Ahead,
        new ImportRunProgressInfo(v.Progress.Car, v.Progress.Cars, v.Progress.Done, v.Progress.Total),
        [.. v.Plan.Cars.Select(c => new ImportRunCarInfo(c.CarId, c.Name, c.TargetVehicleId, c.TargetName))],
        v.Report?.Cars.Select(c => new ImportCarReportInfo(c.CarId, c.Name, c.VehicleId, c.TargetName, c.AsNew, c.LeftOut, c.Refuelings, c.Expenses, c.Schedules, c.Existing, c.Differing, c.InTrash,
            [.. c.Refused.Select(r => new ImportRefusedEntryInfo(r.Kind, r.Date, r.Title, r.Key, JsonSerializer.SerializeToElement(r.Args)))], c.RefusedMore,
            [.. (c.Kinds ?? []).Select(k => new ImportKindReportInfo(k.Kind, k.Mode, k.Imported, k.Changed, k.Deleted))], c.Replacing, c.Links)).ToList(),
        fileToken, limitMinutes);
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ImportRunQueries
{
    /// <summary>The opened file's cars, the importer's own cars one can be added to, and the app's suggestion for each.</summary>
    public async Task<ImportCarOffersInfo> GetBackupImportCars(string token, [Service] BackupImportRunService imports, CancellationToken ct)
    {
        var o = await imports.OffersAsync(token, ct);
        return new ImportCarOffersInfo(
            new BackupFileInfo(o.File.CreatedAt, o.File.Version, o.File.IsExport, [.. o.File.Cars.Select(BackupCarInfo.From)], o.File.TrashedCars),
            [.. o.Cars.Select(c => new ImportCarOfferInfo(BackupCarInfo.From(c.Car), c.OwnedByMe, c.SuggestedTargetId))],
            [.. o.Targets.Select(t => new ImportOwnCarInfo(t.Id, t.Name, t.LicensePlate, t.Units.Distance, t.Units.Volume))]);
    }

    /// <summary>What importing the chosen cars would do, as far as it can be foreseen. Nothing is saved.</summary>
    public async Task<IReadOnlyList<ImportCarReviewInfo>> GetBackupImportReview(string token, IReadOnlyList<ImportCarChoiceInput> cars, [Service] BackupImportRunService imports, CancellationToken ct) =>
        [.. (await imports.ReviewAsync(token, [.. cars.Select(c => c.ToChoice())], ct)).Select(r => new ImportCarReviewInfo(
            r.CarId, r.Name, r.OwnerName, r.OwnedByMe, r.TargetVehicleId, r.TargetName, r.NewRefuelings, r.NewExpenses, r.NewSchedules, r.ExistingRefuelings, r.ExistingExpenses,
            r.ExistingSchedules, r.Differing, r.InTrash,
            [.. r.Kinds.Select(k => new ImportKindReviewInfo(k.Kind, k.Mode, k.Backup, k.New, k.Differ, k.Same, k.InTrash, k.Adds, k.Takes, k.TargetLive, k.TargetTrashed, k.WithPhotos, k.OnTyreLines))],
            r.Links, r.LinksLost))];

    /// <summary>
    /// A page of the Choose dialog: the entries of one kind (<c>refueling</c>, <c>expense</c>, <c>recurring</c>) of a backup's car that are
    /// only in the backup, or with <c>differing</c> the ones that differ from the target's, at most 200 at a time.
    /// </summary>
    public async Task<ImportEntryPageInfo> GetBackupImportEntries(
        string token, Guid carId, Guid targetVehicleId, string kind, bool differing, int skip, int take, [Service] BackupImportRunService imports, CancellationToken ct)
    {
        var page = await imports.EntriesAsync(token, carId, targetVehicleId, kind, differing, skip, take, ct);
        return new ImportEntryPageInfo(page.Total, [.. page.Items.Select(e => new ImportEntryInfo(e.Id, e.Date, e.Odometer, e.Title, e.Amount, e.Currency, e.Volume,
            [.. e.Differences.Select(d => new ImportFieldDifferenceInfo(d.Field, d.Backup, d.Target))]))]);
    }

    /// <summary>A run by its id: its starter's (with the report, kept for a week), or for an administrator one that goes or waits. Null otherwise.</summary>
    public async Task<ImportRunInfo?> GetImportRun(Guid id, [Service] BackupImportRunService imports, [Service] IBackupImportRunner runner, [Service] AccessService access,
        [Service] IOptionsSnapshot<Tankstat.Application.Runs.RunOptions> runs, CancellationToken ct) =>
        await imports.FindAsync(id, ct) is { } v ? ImportRunInfo.From(v, (await access.RequirePrincipalAsync(ct)).Id, v.FileKept ? runner.SessionOf(v.Id) : null, runs.Value.LimitMinutes) : null;

    /// <summary>The signed-in person's run that goes or waits, else their latest one that is still kept; null without one.</summary>
    public async Task<ImportRunInfo?> GetMyImportRun([Service] BackupImportRunService imports, [Service] IBackupImportRunner runner, [Service] AccessService access,
        [Service] IOptionsSnapshot<Tankstat.Application.Runs.RunOptions> runs, CancellationToken ct) =>
        await imports.MineAsync(ct) is { } v ? ImportRunInfo.From(v, (await access.RequirePrincipalAsync(ct)).Id, v.FileKept ? runner.SessionOf(v.Id) : null, runs.Value.LimitMinutes) : null;

    /// <summary>For administrators: every import that goes or waits, the oldest first.</summary>
    public async Task<IReadOnlyList<ImportRunInfo>> GetImportRuns([Service] BackupImportRunService imports, [Service] AccessService access,
        [Service] IOptionsSnapshot<Tankstat.Application.Runs.RunOptions> runs, CancellationToken ct)
    {
        var active = await imports.ActiveAsync(ct);
        var me = (await access.RequirePrincipalAsync(ct)).Id;
        return [.. active.Select(v => ImportRunInfo.From(v, me, null, runs.Value.LimitMinutes))];
    }
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class ImportRunMutations
{
    /// <summary>Queues the import of the chosen cars: it starts by itself, at once or when its turn comes. Ask <c>importRun</c> how it stands.</summary>
    public async Task<ImportRunInfo> StartBackupImport(StartBackupImportInput input, [Service] BackupImportRunService imports, [Service] AccessService access,
        [Service] IOptionsSnapshot<Tankstat.Application.Runs.RunOptions> runs, CancellationToken ct)
    {
        var v = await imports.StartAsync(input.Token, [.. input.Cars.Select(c => c.ToChoice())], ct);
        return ImportRunInfo.From(v, (await access.RequirePrincipalAsync(ct)).Id, input.Token, runs.Value.LimitMinutes);
    }

    /// <summary>Cancels an import that goes or waits: its starter, or an administrator. Nothing of it is kept. False when it is over already.</summary>
    public Task<bool> CancelImportRun(Guid id, [Service] BackupImportRunService imports, CancellationToken ct) => imports.CancelAsync(id, ct);
}
