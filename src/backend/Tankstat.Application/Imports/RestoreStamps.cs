namespace Tankstat.Application.Imports;

/// <summary>The tables whose rows a partial restore stamps (#340): those whose ids it keeps.</summary>
public enum RestoreTable { Vehicle, Refueling, Expense, RecurringExpense, TyreSet, TyreInstallation, TyreEntry, Tyre, TyrePlace, Chart, LogPhoto }

/// <summary>
/// What a restored row takes from the backup: who logged it (null where the table has no creator), its version (null where it has none),
/// and its creation time where the table stores one (schedules, charts, photo rows).
/// </summary>
public sealed record RestoreStamp(Guid Id, Guid? CreatedById, int? Version, DateTimeOffset? CreatedAt);

/// <summary>
/// The last step of a car's partial restore (#340, the owner's decision 4), after the services made its rows: each row the restore made
/// gets the backup's creator, version and creation time, and its last change reads <see cref="Domain.Sync.EntityChange.RestoredFromBackup"/>
/// by the administrator, now (so devices download it). Done after a row's final save, since the replay saves a row several times and each
/// save moves its version. Rows the car had already are never passed.
/// </summary>
public interface IRestoreStamps
{
    Task StampAsync(RestoreTable table, IReadOnlyCollection<RestoreStamp> rows, Guid by, CancellationToken ct);

    /// <summary>The car's rows by table, the trash included: what is there before a restore, and after it, tells what it made.</summary>
    Task<IReadOnlyDictionary<RestoreTable, IReadOnlySet<Guid>>> IdsAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>
    /// Deletes for good the car's places in the trash that the restore itself made and the backup has not (a place the replay made under an
    /// id of its own, trashed when the backup's came in its stead), so the owner never finds them in the tyre trash. Tombstones as any purge.
    /// </summary>
    Task RemoveLeftoverPlacesAsync(Guid vehicleId, IReadOnlyCollection<Guid> placeIds, CancellationToken ct);
}
