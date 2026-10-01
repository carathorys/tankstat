using Tankstat.Domain.Access;

namespace Tankstat.Domain.Vehicles;

public sealed class Vehicle : IOwned, ISoftDeletable
{
    private Vehicle() { } // EF Core

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = "";
    public string? LicensePlate { get; private set; }
    public FuelType FuelType { get; private set; }

    /// <summary>Set while the vehicle is in the trash.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Vehicle Create(Guid ownerId, string name, string? licensePlate, FuelType fuelType)
    {
        var vehicle = new Vehicle { Id = Guid.NewGuid(), OwnerId = ownerId };
        vehicle.Apply(name, licensePlate, fuelType);
        return vehicle;
    }

    public void Update(string name, string? licensePlate, FuelType fuelType)
    {
        if (IsDeleted) throw new DomainException("vehicle.trashedCannotEdit", "A vehicle in the trash cannot be edited; restore it first.");
        Apply(name, licensePlate, fuelType);
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        if (IsDeleted) throw new DomainException("vehicle.alreadyTrashed", "This vehicle is already in the trash.");
        DeletedAt = now;
    }

    public void Restore()
    {
        if (!IsDeleted) throw new DomainException("vehicle.notTrashed", "This vehicle is not in the trash.");
        DeletedAt = null;
    }

    private void Apply(string name, string? licensePlate, FuelType fuelType)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("vehicle.nameRequired", "Vehicle name is required.");
        if (!Enum.IsDefined(fuelType)) throw new DomainException("vehicle.unknownFuelType", $"Unknown fuel type '{fuelType}'.", new { Value = fuelType.ToString() });

        Name = name.Trim();
        LicensePlate = string.IsNullOrWhiteSpace(licensePlate) ? null : licensePlate.Trim().ToUpperInvariant();
        FuelType = fuelType;
    }
}
