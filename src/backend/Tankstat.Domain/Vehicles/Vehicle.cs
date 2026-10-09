using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Sync;

namespace Tankstat.Domain.Vehicles;

public sealed class Vehicle : IOwned, ISoftDeletable, ISynced
{
    private Vehicle() { } // EF Core

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = "";
    public string? LicensePlate { get; private set; }
    public FuelType FuelType { get; private set; }

    /// <summary>The units of this vehicle's distance and fuel volume; all its logs use them.</summary>
    public MeasurementUnits Units { get; private set; } = MeasurementUnits.Metric;

    /// <summary>The vehicle's picture (a <c>StoredImage</c>), if one was uploaded.</summary>
    public Guid? PictureImageId { get; private set; }

    /// <summary>Set while the vehicle is in the trash.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Counts the saves of what a client can edit (the fields, the trash), from 1: a client that edits an old copy says which version it
    /// started from, so a change made meanwhile is noticed. The picture does not count.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>When it was last saved (set by the persistence layer, see <see cref="ISynced"/>): a device that keeps a copy downloads it again.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <inheritdoc cref="ISynced.ChangedAt"/>
    public DateTimeOffset? ChangedAt { get; private set; }

    /// <inheritdoc cref="ISynced.ChangedById"/>
    public Guid? ChangedById { get; private set; }

    /// <inheritdoc cref="ISynced.LastChange"/>
    public EntityChange? LastChange { get; private set; }

    /// <summary>The version it was loaded with, before this instance counted any save: the save is made only if nobody saved it meanwhile.</summary>
    public int SavedVersion => _loadedVersion ?? Version;

    private int? _loadedVersion;

    void ISynced.Saved() => _loadedVersion = null;

    private void Bump(EntityChange what, Guid? by)
    {
        _loadedVersion ??= Version;
        Version++;
        (LastChange, ChangedById) = (what, by);
    }

    Guid ISynced.SyncVehicleId => Id;
    OfflineEntityType ISynced.SyncType => OfflineEntityType.Vehicle;

    /// <param name="id">The id the client chose beforehand, if any (see <see cref="EntityId"/>).</param>
    public static Vehicle Create(Guid ownerId, string name, string? licensePlate, FuelType fuelType, MeasurementUnits units, Guid? id = null)
    {
        var vehicle = new Vehicle { Id = EntityId.OrNew(id), OwnerId = ownerId, Version = 1, LastChange = EntityChange.Created, ChangedById = ownerId };
        vehicle.Apply(name, licensePlate, fuelType, units);
        return vehicle;
    }

    /// <param name="by">Who changed it (see <see cref="ChangedById"/>).</param>
    public void Update(string name, string? licensePlate, FuelType fuelType, MeasurementUnits units, Guid? by = null)
    {
        if (IsDeleted) throw new DomainException("vehicle.trashedCannotEdit", "A vehicle in the trash cannot be edited; restore it first.");
        Apply(name, licensePlate, fuelType, units);
        Bump(EntityChange.Edited, by);
    }

    public void SetPicture(Guid? imageId) => PictureImageId = imageId;

    public void MarkDeleted(DateTimeOffset now, Guid? by = null)
    {
        if (IsDeleted) throw new DomainException("vehicle.alreadyTrashed", "This vehicle is already in the trash.");
        DeletedAt = now;
        Bump(EntityChange.Trashed, by);
    }

    public void Restore(Guid? by = null)
    {
        if (!IsDeleted) throw new DomainException("vehicle.notTrashed", "This vehicle is not in the trash.");
        DeletedAt = null;
        Bump(EntityChange.Restored, by);
    }

    private void Apply(string name, string? licensePlate, FuelType fuelType, MeasurementUnits units)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("vehicle.nameRequired", "Vehicle name is required.");
        if (!Enum.IsDefined(fuelType)) throw new DomainException("vehicle.unknownFuelType", $"Unknown fuel type '{fuelType}'.", new { Value = fuelType.ToString() });

        Name = name.Trim();
        LicensePlate = string.IsNullOrWhiteSpace(licensePlate) ? null : licensePlate.Trim().ToUpperInvariant();
        FuelType = fuelType;
        Units = units;
    }
}
