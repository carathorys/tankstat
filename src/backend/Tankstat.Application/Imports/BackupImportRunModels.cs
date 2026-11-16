using System.Text.Json;
using System.Text.Json.Serialization;
using Tankstat.Domain.Imports;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Imports;

/// <summary>What happens to a car when one of its entries cannot be saved (the owner's decision 5: the rest by default).</summary>
public enum RefusedEntryPolicy { ImportTheRest, LeaveCarOut }

/// <summary>A car the person chose: as a new car (<see cref="TargetVehicleId"/> null), or added to one of their own.</summary>
public sealed record ImportCarChoice(Guid CarId, Guid? TargetVehicleId, RefusedEntryPolicy Policy = RefusedEntryPolicy.ImportTheRest);

/// <summary>A chosen car in a run's plan, with the names it had when the person pressed Import.</summary>
public sealed record ImportCarPlan(Guid CarId, string Name, Guid? TargetVehicleId, string? TargetName, RefusedEntryPolicy Policy, int Entries);

/// <summary>What a run is to do.</summary>
public sealed record ImportRunPlan(IReadOnlyList<ImportCarPlan> Cars);

/// <summary>
/// An entry that could not be saved: what it is (its kind, its day, a title for an expense or a schedule), and the reason as the server
/// keys it everywhere, with the values the reason's text is filled with.
/// </summary>
public sealed record ImportRefusedEntry(string Kind, DateOnly? Date, string? Title, string Key, IReadOnlyDictionary<string, string> Args);

/// <summary>
/// What came of one car. <see cref="LeftOut"/>: an entry was refused and the car's policy left it out whole (nothing of it came in;
/// <see cref="Refused"/> holds that entry). <see cref="RefusedMore"/>: refused entries past the ones listed.
/// </summary>
public sealed record ImportCarReport(
    Guid CarId, string Name, Guid? VehicleId, string? TargetName, bool AsNew, bool LeftOut, int Refuelings, int Expenses, int Schedules,
    int Existing, int Differing, int InTrash, IReadOnlyList<ImportRefusedEntry> Refused, int RefusedMore);

public sealed record ImportRunReport(IReadOnlyList<ImportCarReport> Cars)
{
    /// <summary>A car lists at most this many refused entries; the rest are counted.</summary>
    public const int MaxRefusedPerCar = 100;

    public int Imported => Cars.Count(c => !c.LeftOut);
    public int Refused => Cars.Sum(c => c.Refused.Count + c.RefusedMore);
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
    int NewRefuelings, int NewExpenses, int NewSchedules, int ExistingRefuelings, int ExistingExpenses, int ExistingSchedules, int Differing, int InTrash);
