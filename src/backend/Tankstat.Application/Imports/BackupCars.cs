using Tankstat.Application.Expenses;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Recurring;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>Where a backup-shaped file being read comes from: a person's upload, or one of the server's restore points (administrators).</summary>
public enum BackupFileOrigin { Upload, RestorePoint }

/// <summary>Where the opening of a file stands.</summary>
public enum BackupImportState
{
    /// <summary>The file is encrypted and waits for its passphrase (a restore point: or its identity).</summary>
    NeedsKey,
    /// <summary>It is being opened: unpacked and loaded. A large one takes longer than a request may.</summary>
    Opening,
    /// <summary>Its cars can be read.</summary>
    Opened,
    /// <summary>It was refused or its tries ran out: the file is gone from the server, and <see cref="BackupImportStatus.Refusal"/> says why.</summary>
    Ended,
}

/// <summary>A car in the file: what it is, whose it was (the name only), and how much of each kind it has (what was in the trash left out).</summary>
public sealed record BackupCarSummary(
    Guid Id, string Name, string? LicensePlate, FuelType FuelType, MeasurementUnits Units, Guid OwnerId, string? OwnerName, int Refuelings, int Expenses, int Schedules);

/// <summary>What an opened file holds. <see cref="Version"/> is the file's own word, cut to a release's shape: shown as text only.</summary>
public sealed record BackupFileSummary(DateTimeOffset CreatedAt, string Version, bool IsExport, IReadOnlyList<BackupCarSummary> Cars, int TrashedCars);

/// <summary>Why a file was refused: a key the client translates, with its arguments.</summary>
public sealed record BackupImportRefusal(string Key, IReadOnlyDictionary<string, object?> Args);

/// <summary>A person's open file as they see it. <see cref="TriesLeft"/>: for an upload that needs its key; null where tries are not counted.</summary>
public sealed record BackupImportStatus(
    string Token, BackupFileOrigin Origin, BackupImportState State, int? TriesLeft, bool WrongKey, BackupFileSummary? Summary, BackupImportRefusal? Refusal);

/// <summary>A refuelling of a backup's car with its id, as the service takes one.</summary>
public sealed record BackupRefueling(Guid Id, RefuelingInput Values);

/// <summary>An expense of a backup's car. <see cref="CompletedScheduleIds"/>: the schedules its visit marked done (the link comes only when both kinds do).</summary>
public sealed record BackupExpense(Guid Id, ExpenseInput Values, IReadOnlyList<Guid> CompletedScheduleIds);

/// <summary>A schedule of a backup's car.</summary>
public sealed record BackupSchedule(Guid Id, RecurringExpenseInput Values);

/// <summary>A car of the file with its logs and schedules (nothing that was in the trash).</summary>
public sealed record BackupCar(BackupCarSummary Car, IReadOnlyList<BackupRefueling> Refuelings, IReadOnlyList<BackupExpense> Expenses, IReadOnlyList<BackupSchedule> Schedules);

/// <summary>The cars of an opened file. It lives as long as its session.</summary>
public interface IBackupCars
{
    BackupFileSummary Summary { get; }

    /// <summary>The car with its contents; null when the file has no such car (or it was in the trash).</summary>
    Task<BackupCar?> ReadCarAsync(Guid carId, CancellationToken ct);
}

/// <summary>What a service's entity is to the reader: the same shapes a backup's rows are read into, so the two can be compared.</summary>
public static class BackupCarShapes
{
    public const int MaxTextLength = 4000;

    /// <summary>Text out of a file is the uploader's: cut to a length nothing in the app exceeds.</summary>
    public static string? Cut(string? text) => text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;

    public static BackupRefueling Of(Refueling r) => new(r.Id, new RefuelingInput(
        r.Date, r.Volume, r.TotalCost, r.Currency, r.Odometer, r.IsFullTank, Cut(r.Note), r.MissedPreviousFillUp, r.Cost?.Discount));

    public static BackupExpense Of(Expense e, IReadOnlyList<Guid> completed) => new(e.Id, new ExpenseInput(
        e.Date, Cut(e.Title)!, Cut(e.Category), e.Amount, e.Currency, e.Odometer, Cut(e.Note), e.Cost?.Discount), completed);

    public static BackupSchedule Of(RecurringExpense s) => new(s.Id, new RecurringExpenseInput(
        Cut(s.Title)!, Cut(s.Category), Cut(s.Note), s.Kind, s.IntervalMonths, s.IntervalDistance, s.LastDoneDate, s.LastDoneOdometer, s.WarnDays, s.WarnDistance));
}
