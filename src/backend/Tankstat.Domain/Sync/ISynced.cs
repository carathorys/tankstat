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
}
