using Tankstat.Domain.Access;

namespace Tankstat.Domain.Vehicles;

public sealed class Refueling : IOwned
{
    private Refueling() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Always the owner of the vehicle, denormalized so access filtering works on this table directly.</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Liters { get; private set; }
    public decimal TotalCost { get; private set; }
    public int OdometerKm { get; private set; }
    public bool IsFullTank { get; private set; }

    public static Refueling Create(Guid ownerId, Guid vehicleId, DateOnly date, decimal liters, decimal totalCost, int odometerKm, bool isFullTank)
    {
        if (liters <= 0) throw new DomainException("Liters must be greater than zero.");
        if (totalCost < 0) throw new DomainException("Total cost cannot be negative.");
        if (odometerKm < 0) throw new DomainException("Odometer cannot be negative.");

        return new Refueling
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            VehicleId = vehicleId,
            Date = date,
            Liters = liters,
            TotalCost = totalCost,
            OdometerKm = odometerKm,
            IsFullTank = isFullTank,
        };
    }
}
