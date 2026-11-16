using System.Text.Json;
using Microsoft.Extensions.Options;
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
