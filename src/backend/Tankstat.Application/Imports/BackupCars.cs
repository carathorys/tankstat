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

/// <summary>
/// A car in the file: what it is, whose it was (the name; the e-mail only from a restore point, #340: the owner picker's suggestion), and
/// how much of each kind it has (what was in the trash left out).
/// </summary>
public sealed record BackupCarSummary(
    Guid Id, string Name, string? LicensePlate, FuelType FuelType, MeasurementUnits Units, Guid OwnerId, string? OwnerName, int Refuelings, int Expenses, int Schedules,
    string? OwnerEmail = null);

/// <summary>What an opened file holds. <see cref="Version"/> is the file's own word, cut to a release's shape: shown as text only.</summary>
public sealed record BackupFileSummary(DateTimeOffset CreatedAt, string Version, bool IsExport, IReadOnlyList<BackupCarSummary> Cars, int TrashedCars);

/// <summary>Why a file was refused: a key the client translates, with its arguments.</summary>
public sealed record BackupImportRefusal(string Key, IReadOnlyDictionary<string, object?> Args);

/// <summary>A person's open file as they see it. <see cref="TriesLeft"/>: for an upload that needs its key; null where tries are not counted.</summary>
public sealed record BackupImportStatus(
    string Token, BackupFileOrigin Origin, BackupImportState State, int? TriesLeft, bool WrongKey, BackupFileSummary? Summary, BackupImportRefusal? Refusal);

/// <summary>An entry of a backup's car (or of a car that is here, read into the same shape): its id there.</summary>
public interface IBackupEntry
{
    Guid Id { get; }
}

/// <summary>A refuelling of a backup's car with its id, as the service takes one.</summary>
public sealed record BackupRefueling(Guid Id, RefuelingInput Values) : IBackupEntry;

/// <summary>An expense of a backup's car. <see cref="CompletedScheduleIds"/>: the schedules its visit marked done (the link comes only when both kinds do).</summary>
public sealed record BackupExpense(Guid Id, ExpenseInput Values, IReadOnlyList<Guid> CompletedScheduleIds) : IBackupEntry;

/// <summary>A schedule of a backup's car.</summary>
public sealed record BackupSchedule(Guid Id, RecurringExpenseInput Values) : IBackupEntry;

/// <summary>
/// A photo of one of a backup car's logs: which log, the image, and when it was added (the order a log shows its photos in); its own id
/// and who added it (a partial restore keeps both, #340).
/// </summary>
public sealed record BackupPhoto(Domain.Photos.LogType LogType, Guid LogId, Guid ImageId, DateTimeOffset CreatedAt, Guid Id = default, Guid CreatedById = default);

/// <summary>A person the car's logs were shared with in the backup (#340), with their name there and the level they had.</summary>
public sealed record BackupGrant(Guid GranteeId, string? Name, Domain.Access.AccessLevel Level);

/// <summary>
/// What a partial restore takes from the backup besides the values (#340): every row's stamps by table and id (the trash included), and the
/// car's sharing of its logs.
/// </summary>
public sealed record BackupRestoreData(IReadOnlyDictionary<RestoreTable, IReadOnlyDictionary<Guid, RestoreStamp>> Stamps, IReadOnlyList<BackupGrant> Grants)
{
    public RestoreStamp? StampOf(RestoreTable table, Guid id) => Stamps.TryGetValue(table, out var rows) && rows.TryGetValue(id, out var stamp) ? stamp : null;
}

/// <summary>A person of the file as the reader keeps one: the name, and the e-mail only from a restore point.</summary>
public sealed record BackupUser(Guid Id, string? Name, string? Email);

/// <summary>A dashboard of a backup's car: the vehicle's (<see cref="UserId"/> null) or a person's own.</summary>
public sealed record BackupDashboard(Guid? UserId, Domain.Charts.DashboardLayoutValues Values);

/// <summary>
/// What else a backup's car holds (#339): its picture's image id, the photos of its live logs in the order they are shown, whether the
/// backup was made without photos, its saved widgets and its dashboards.
/// </summary>
public sealed record BackupMedia(
    Guid? PictureImageId, IReadOnlyList<BackupPhoto> Photos, bool PhotosLeftOut, IReadOnlyList<Domain.Charts.VehicleChart> Charts, IReadOnlyList<BackupDashboard> Dashboards);

/// <summary>A car of the file with its logs and schedules (nothing that was in the trash).</summary>
public sealed record BackupCar(BackupCarSummary Car, IReadOnlyList<BackupRefueling> Refuelings, IReadOnlyList<BackupExpense> Expenses, IReadOnlyList<BackupSchedule> Schedules);

/// <summary>The cars of an opened file. It lives as long as its session.</summary>
public interface IBackupCars
{
    BackupFileSummary Summary { get; }

    /// <summary>The car with its contents; null when the file has no such car (or it was in the trash).</summary>
    Task<BackupCar?> ReadCarAsync(Guid carId, CancellationToken ct);

    /// <summary>
    /// What else the car holds (#339): its picture, the photos of its logs (none when the backup was made without them), its saved widgets
    /// (every one: who may have which is the import's to decide) and its dashboards, the vehicle's and the people's own. Null when the
    /// file has no such car.
    /// </summary>
    Task<BackupMedia?> ReadMediaAsync(Guid carId, CancellationToken ct);

    /// <summary>
    /// One image's bytes out of the file: a photo of one of the car's logs, or (<paramref name="photo"/> null) the car's picture, found by
    /// the ids alone (never by a folder the file names). Null when the file has it not; more than <paramref name="maxBytes"/> are not read
    /// (the bytes then are one longer, for the caller to refuse).
    /// </summary>
    Task<byte[]?> ReadImageAsync(Guid carId, BackupPhoto? photo, Guid imageId, int maxBytes, CancellationToken ct);

    /// <summary>
    /// The car's tyre history (#339) as the tyre service reads a car's: its sets, their times on the car, the tyre log's lines, single tyres
    /// and their places, the trash included (<see cref="Tyres.TyreBook.IsLive"/> tells), and the expenses the lines hold. Null when the file
    /// has no such car.
    /// </summary>
    Task<Tyres.TyreBook?> ReadTyresAsync(Guid carId, CancellationToken ct);

    /// <summary>A partial restore's (#340): the car's rows' stamps and its sharing. Null when the file has no such car.</summary>
    Task<BackupRestoreData?> ReadRestoreAsync(Guid carId, CancellationToken ct);

    /// <summary>The file's people by id (the ones it has; names and e-mails cut), for the owner picker and the review (#340).</summary>
    Task<IReadOnlyDictionary<Guid, BackupUser>> ReadUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
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
