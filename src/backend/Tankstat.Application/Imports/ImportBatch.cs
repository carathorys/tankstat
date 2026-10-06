using Tankstat.Domain.Odometers;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>What the source file says about the vehicle (every part optional; a parser fills what its format knows).</summary>
public sealed record ImportedVehicle(string? Name, string? LicensePlate, FuelType? FuelType, DistanceUnit? Distance, VolumeUnit? Volume);

/// <param name="SourceRow">1-based row of the record in its section of the file, for error messages.</param>
/// <param name="MissedPreviousFillUp">The file says a fill-up before this one was not logged.</param>
public sealed record ImportedFuelLog(
    int SourceRow, DateOnly Date, long Odometer, decimal Volume, decimal TotalCost, bool IsFullTank, bool MissedPreviousFillUp, string? Note);

public sealed record ImportedExpense(int SourceRow, DateOnly Date, string Title, string? Category, decimal Amount, long? Odometer, string? Note);

/// <summary>A repeating cost (insurance, oil change) from the file: what it repeats by and where it was last done. Its amount is not kept.</summary>
public sealed record ImportedRecurring(
    int SourceRow, string Title, string? Category, string? Note, RecurrenceKind Kind, int? IntervalMonths, long? IntervalDistance,
    DateOnly LastDoneDate, long? LastDoneOdometer);

/// <summary>A row that was skipped or a problem found while reading (a stable <see cref="Key"/> and its arguments, translated by the client).</summary>
public sealed record ImportIssue(string Section, int Row, string Key, IReadOnlyDictionary<string, object?> Args);

/// <summary>What a parser made of a file, independent of the format: the common shape every importer produces.</summary>
public sealed record ImportBatch(
    string Format,
    ImportedVehicle? Vehicle,
    IReadOnlyList<ImportedFuelLog> FuelLogs,
    IReadOnlyList<ImportedExpense> Expenses,
    IReadOnlyList<ImportedRecurring> Recurring,
    IReadOnlyList<ImportIssue> Issues);

/// <summary>
/// Reads one file format (Fuelio CSV today; other CSV or JSON sources later) into the common <see cref="ImportBatch"/>. Parsers do not
/// touch the database or check business rules (odometer order, access): the import service does that for every format alike.
/// </summary>
public interface IImportParser
{
    /// <summary>The identifier clients use to pick this format, e.g. <c>fuelio</c>.</summary>
    string Format { get; }

    /// <summary>Throws a <c>DomainException</c> (<c>import.unreadable</c>) when the file is not in this format.</summary>
    ImportBatch Parse(Stream content);
}
