using System.Text.Json;
using System.Text.Json.Serialization;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>What happens to a car when one of its entries cannot be saved (the owner's decision 5: the rest by default).</summary>
public enum RefusedEntryPolicy { ImportTheRest, LeaveCarOut }

/// <summary>
/// What comes of one kind of a car's entries (#338; refuellings, expenses, recurring expenses): only what is missing (as #337 did for
/// everything), nothing, the target's deleted for good and the backup's in their place, or entry by entry.
/// </summary>
public enum ImportKindMode { OnlyMissing, DontImport, ReplaceAll, Choose }

/// <summary>
/// A kind's choice. For <see cref="ImportKindMode.Choose"/> the picks travel as a default and its exceptions, never as every id:
/// <see cref="AddNew"/> says whether the entries only in the backup are added, with <see cref="NewExceptions"/> the ones for which it is
/// the other way; <see cref="TakeBackups"/> whether the entries that differ take the backup's values, with <see cref="DifferExceptions"/>.
/// The ids are the backup's own. An entry that differs and that nobody chose keeps the target's values.
/// </summary>
public sealed record ImportKindChoice(
    ImportKindMode Mode = ImportKindMode.OnlyMissing, bool AddNew = true, IReadOnlyList<Guid>? NewExceptions = null, bool TakeBackups = false, IReadOnlyList<Guid>? DifferExceptions = null)
{
    public static readonly ImportKindChoice Missing = new();

    [JsonIgnore] // worked out: not kept in a run's plan
    public int Exceptions => (NewExceptions?.Count ?? 0) + (DifferExceptions?.Count ?? 0);
}

/// <summary>The three kinds' choices for a car; a kind left out is <see cref="ImportKindChoice.Missing"/>.</summary>
public sealed record ImportCarKinds(ImportKindChoice? Refuelings = null, ImportKindChoice? Expenses = null, ImportKindChoice? Schedules = null)
{
    public static readonly ImportCarKinds Missing = new();

    [JsonIgnore] // worked out: kept in a run's plan, it would write every exception's id twice
    public IEnumerable<ImportKindChoice> All => [Refuelings ?? ImportKindChoice.Missing, Expenses ?? ImportKindChoice.Missing, Schedules ?? ImportKindChoice.Missing];
}

/// <summary>
/// A car the person chose: as a new car (<see cref="TargetVehicleId"/> null), or added to one of their own. <see cref="Kinds"/>: what comes
/// of each kind (null: only the missing, #337's way). <see cref="ConfirmedName"/>: the target's name as the person typed it, which a
/// <i>Replace all</i> asks for.
/// </summary>
public sealed record ImportCarChoice(
    Guid CarId, Guid? TargetVehicleId, RefusedEntryPolicy Policy = RefusedEntryPolicy.ImportTheRest, ImportCarKinds? Kinds = null, string? ConfirmedName = null);

/// <summary>A chosen car in a run's plan, with the names it had when the person pressed Import.</summary>
public sealed record ImportCarPlan(Guid CarId, string Name, Guid? TargetVehicleId, string? TargetName, RefusedEntryPolicy Policy, int Entries, ImportCarKinds? Kinds = null)
{
    /// <summary>A <i>Replace all</i> into an own car: one refusal puts the whole car back (the owner's decision 1).</summary>
    [JsonIgnore]
    public bool Replacing => TargetVehicleId is not null && (Kinds ?? ImportCarKinds.Missing).All.Any(k => k.Mode == ImportKindMode.ReplaceAll);
}

/// <summary>What a run is to do.</summary>
public sealed record ImportRunPlan(IReadOnlyList<ImportCarPlan> Cars);

/// <summary>
/// An entry that could not be saved: what it is (its kind, its day, a title for an expense or a schedule), and the reason as the server
/// keys it everywhere, with the values the reason's text is filled with.
/// </summary>
public sealed record ImportRefusedEntry(string Kind, DateOnly? Date, string? Title, string Key, IReadOnlyDictionary<string, string> Args)
{
    /// <summary>The <see cref="Kind"/> of a refusal that is the car's own, not an entry's: the car was left out for it, whatever its policy.</summary>
    public const string Car = "car";
}

/// <summary>
/// What came of one car. <see cref="LeftOut"/>: an entry was refused and the car's policy left it out whole (nothing of it came in;
/// <see cref="Refused"/> holds that entry). <see cref="RefusedMore"/>: refused entries past the ones listed.
/// </summary>
public sealed record ImportCarReport(
    Guid CarId, string Name, Guid? VehicleId, string? TargetName, bool AsNew, bool LeftOut, int Refuelings, int Expenses, int Schedules,
    int Existing, int Differing, int InTrash, IReadOnlyList<ImportRefusedEntry> Refused, int RefusedMore,
    IReadOnlyList<ImportKindReport>? Kinds = null, bool Replacing = false, int Links = 0);

/// <summary>
/// What came of one kind of a car (#338): how it was to be done, how many of the backup's were added, how many of the car's took the
/// backup's values, and how many of the car's were deleted for good first (the number the run met, not the review's).
/// </summary>
public sealed record ImportKindReport(string Kind, ImportKindMode Mode, int Imported, int Changed, int Deleted);

public sealed record ImportRunReport(IReadOnlyList<ImportCarReport> Cars)
{
    /// <summary>A car lists at most this many refused entries; the rest are counted.</summary>
    public const int MaxRefusedPerCar = 100;

    public int Imported => Cars.Count(c => !c.LeftOut);
    /// <summary>The entries that could not be saved; a car left out for a refusal of its own (<see cref="ImportRefusedEntry.Car"/>) is no entry.</summary>
    public int Refused => Cars.Sum(c => c.Refused.Count(r => r.Kind != ImportRefusedEntry.Car) + c.RefusedMore);
}

/// <summary>How far a run is.</summary>
public sealed record ImportRunProgress(int Car, int Cars, int Done, int Total);

/// <summary>A run as people see it: its starter always, an administrator while it goes or waits (without the report).</summary>
public sealed record ImportRunView(
    Guid Id, ImportRunKind Kind, Guid StartedById, string? StartedByName, ImportRunState State, ImportRunFailure? Failure, Guid? CancelledById,
    DateTimeOffset QueuedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, int Ahead, ImportRunProgress Progress, ImportRunPlan Plan, ImportRunReport? Report,
    bool FileKept);

public static class ImportRunJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null; // a row of a release that wrote it otherwise: shown without it
        }
    }
}

/// <summary>A car that is here, as the import's target: the person's own, never one only shared with them.</summary>
public sealed record ImportOwnCar(Guid Id, string Name, string? LicensePlate, FuelType FuelType, Domain.Measurements.MeasurementUnits Units);

/// <summary>
/// The file's cars with what the person can do with each: the cars of their own to add one to, and the one the app suggests (the same
/// plate, else the same name). <see cref="OwnedByMe"/>: the backup's owner of the car is the person importing (else the page names them).
/// </summary>
public sealed record ImportCarOffer(BackupCarSummary Car, bool OwnedByMe, Guid? SuggestedTargetId);

public sealed record ImportCarOffers(BackupFileSummary File, IReadOnlyList<ImportCarOffer> Cars, IReadOnlyList<ImportOwnCar> Targets);

/// <summary>What importing a chosen car would do, as far as it can be foreseen: counts per kind of what is new, what exists, what differs, what is in the target's trash.</summary>
public sealed record ImportCarReview(
    Guid CarId, string Name, string? OwnerName, bool OwnedByMe, Guid? TargetVehicleId, string? TargetName,
    int NewRefuelings, int NewExpenses, int NewSchedules, int ExistingRefuelings, int ExistingExpenses, int ExistingSchedules, int Differing, int InTrash,
    IReadOnlyList<ImportKindReview> Kinds, bool Links, int LinksLost);

/// <summary>
/// One kind of a chosen car, as the review and the Cars step say it (#338). Of the backup's <see cref="Backup"/> entries: <see cref="New"/>
/// only there, <see cref="Differ"/>, <see cref="Same"/>, <see cref="InTrash"/> matching the target's trash. By the choice: <see cref="Adds"/>
/// will be added, <see cref="Takes"/> of the target's take the backup's values. The target has <see cref="TargetLive"/> and
/// <see cref="TargetTrashed"/> of the kind, of which <see cref="WithPhotos"/> and <see cref="OnTyreLines"/> hold a <i>Replace all</i> back.
/// </summary>
public sealed record ImportKindReview(
    string Kind, ImportKindMode Mode, int Backup, int New, int Differ, int Same, int InTrash, int Adds, int Takes, int TargetLive, int TargetTrashed, int WithPhotos, int OnTyreLines);

/// <summary>
/// An entry of the backup for the <i>Choose…</i> dialog: what tells it apart, and for one that differs both sides of each field that does
/// (as text, invariant; a day as yyyy-MM-dd, an enum by its GraphQL name).
/// </summary>
public sealed record ImportEntryView(Guid Id, DateOnly? Date, long? Odometer, string? Title, decimal? Amount, string? Currency, decimal? Volume, IReadOnlyList<ImportFieldDifference> Differences);

public sealed record ImportFieldDifference(string Field, string? Backup, string? Target);

/// <summary>A page of a section of the <i>Choose…</i> dialog, with how many the section has.</summary>
public sealed record ImportEntryPage(int Total, IReadOnlyList<ImportEntryView> Items);
