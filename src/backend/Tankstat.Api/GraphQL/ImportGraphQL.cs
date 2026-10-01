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
    ImportedVehicleInfo? SourceVehicle, int FuelRows, int ExpenseRows, int DuplicateFuelRows, int DuplicateExpenseRows,
    DateOnly? FirstDate, DateOnly? LastDate, IReadOnlyList<string> Categories, IReadOnlyList<ImportIssueInfo> Issues);

public sealed record ImportResultInfo(
    Guid VehicleId, int FuelImported, int ExpensesImported, int FuelSkippedDuplicates, int ExpensesSkippedDuplicates, IReadOnlyList<ImportIssueInfo> Errors);

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
            p.FuelRows, p.ExpenseRows, p.DuplicateFuelRows, p.DuplicateExpenseRows, p.FirstDate, p.LastDate, p.Categories,
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
        return new ImportResultInfo(r.VehicleId, r.FuelImported, r.ExpensesImported, r.FuelSkippedDuplicates, r.ExpensesSkippedDuplicates,
            r.Errors.Select(ImportIssueInfo.From).ToList());
    }
}
