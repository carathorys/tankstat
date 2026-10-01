using Tankstat.Domain.Access;

namespace Tankstat.Domain.Odometers;

/// <summary>
/// One reading of a vehicle's odometer at a date, in the unit of the vehicle. Entities that record an odometer
/// (refuelings today, inspections later) do not carry a number of their own: they link to a reading. A vehicle has
/// a single odometer, so all its readings, whatever recorded them, must agree with each other over time (see
/// <c>OdometerService</c>). A reading shares the lifecycle of the entity that owns it (trash, restore, delete).
/// </summary>
public sealed class OdometerReading : IOwned, ISoftDeletable
{
    private OdometerReading() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>The owner of the vehicle (denormalized, like on the logs).</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public DateOnly Date { get; private set; }
    public long Value { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static OdometerReading Create(Guid ownerId, Guid vehicleId, DateOnly date, long value) =>
        new() { Id = Guid.NewGuid(), OwnerId = ownerId, VehicleId = vehicleId, Date = date, Value = OdometerValue.From(value).Value };

    public void Update(DateOnly date, long value)
    {
        Date = date;
        Value = OdometerValue.From(value).Value;
    }

    public void MarkDeleted(DateTimeOffset now) => DeletedAt = now;

    public void Restore() => DeletedAt = null;
}
