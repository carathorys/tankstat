namespace Tankstat.Domain.Sync;

/// <summary>The kinds of entity a device keeps a copy of (the offline feed, its tombstones).</summary>
public enum OfflineEntityType
{
    Vehicle,
    Refueling,
    Expense,
    RecurringExpense,
}

/// <summary>
/// An entity a device keeps a copy of. <see cref="UpdatedAt"/> marks anything a reader must download again: it is set on every save by the
/// persistence layer (and by the few writes that bypass it), unlike <c>Version</c>, which only counts what a writer can conflict with.
/// Removing one for good leaves a <see cref="Tombstone"/>.
/// </summary>
public interface ISynced
{
    Guid Id { get; }

    /// <summary>The vehicle it belongs to; a vehicle's own id for a vehicle.</summary>
    Guid SyncVehicleId { get; }

    OfflineEntityType SyncType { get; }

    DateTimeOffset UpdatedAt { get; }

    /// <summary>Counts the saves of what a writer can conflict with.</summary>
    int Version { get; }

    /// <summary>
    /// The version as it was loaded, before this instance counted a save: the persistence layer saves only if the stored row still has it
    /// (a conditional write), so two changes made from the same version cannot both be saved.
    /// </summary>
    int SavedVersion { get; }

    /// <summary>The persistence layer saved it: what it holds now is what is stored, so its next save starts from this version.</summary>
    void Saved();
}
