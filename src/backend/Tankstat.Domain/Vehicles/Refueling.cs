using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;

namespace Tankstat.Domain.Vehicles;

/// <summary>
/// A fill-up. The volume is in the vehicle's volume unit (<see cref="Vehicle.Units"/>). The odometer and the cost are not numbers of
/// their own but links to an <see cref="OdometerReading"/> and a <see cref="Cost"/> that this log owns (they are trashed, restored and
/// deleted with it).
/// </summary>
public sealed class Refueling : IOwned, ISoftDeletable
{
    public const int MaxNoteLength = 500;

    private Refueling() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Always the owner of the vehicle, denormalized so access filtering works on this table directly.</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }

    /// <summary>Who logged it (the anonymous owner when authentication is off).</summary>
    public Guid CreatedById { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>In the vehicle's volume unit (litres or gallons).</summary>
    public decimal Volume { get; private set; }

    public Guid CostId { get; private set; }

    /// <summary>The linked cost (amount and currency); load it (Include) before reading <see cref="TotalCost"/> or <see cref="Currency"/>.</summary>
    public Cost Cost { get; private set; } = null!;

    /// <summary>What the fill-up cost, in <see cref="Currency"/>.</summary>
    public decimal TotalCost => Cost.Amount;

    /// <summary>The currency this fill-up was paid in (it can differ from log to log, e.g. when travelling).</summary>
    public string Currency => Cost.Currency;
    public bool IsFullTank { get; private set; }
    public string? Note { get; private set; }

    public Guid OdometerReadingId { get; private set; }

    /// <summary>The linked odometer reading; load it (Include) before reading <see cref="Odometer"/>.</summary>
    public OdometerReading OdometerReading { get; private set; } = null!;

    /// <summary>The odometer value, in the vehicle's distance unit.</summary>
    public long Odometer => OdometerReading.Value;

    /// <summary>
    /// Fuel used per 100 distance units, between the previous full fill-up and this one (litres per 100 km, gallons per 100 miles, ...),
    /// or null when it cannot be known: the log is not a full fill-up, no earlier full fill-up exists, or the odometer did not advance.
    /// It is stored so it is not recalculated on every read; <see cref="ConsumptionCalculator"/> refreshes it whenever the vehicle's logs change.
    /// </summary>
    public decimal? Consumption { get; private set; }

    /// <summary>Only <see cref="ConsumptionCalculator"/> should call this (consumption depends on the neighbouring logs, not on this log alone).</summary>
    public void SetConsumption(decimal? value) => Consumption = value is { } v ? Math.Round(v, 3) : null;

    /// <summary>Set while the log is in the trash.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Total cost divided by volume (derived, not stored).</summary>
    public decimal PricePerUnit => Volume == 0 ? 0 : Math.Round(Cost.Amount / Volume, 3);

    /// <param name="reading">The reading of the odometer at this fill-up; created for this log, belonging to the same vehicle.</param>
    public static Refueling Create(
        Guid ownerId, Guid createdById, Guid vehicleId, DateOnly date, decimal volume, Cost cost, OdometerReading reading, bool isFullTank, string? note = null)
    {
        if (reading.VehicleId != vehicleId || cost.VehicleId != vehicleId)
            throw new DomainException("refueling.wrongVehicle", "The odometer reading and the cost must belong to the same vehicle as the log.");

        var refueling = new Refueling
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, CreatedById = createdById, VehicleId = vehicleId,
            OdometerReadingId = reading.Id, OdometerReading = reading, CostId = cost.Id, Cost = cost,
        };
        refueling.Apply(date, volume, isFullTank, note);
        reading.Update(date, reading.Value);
        cost.Update(date, cost.Amount, cost.Currency);
        return refueling;
    }

    /// <summary>Changes the log and keeps its odometer reading (date and value) in step.</summary>
    public void Update(DateOnly date, decimal volume, decimal totalCost, string currency, long odometer, bool isFullTank, string? note)
    {
        if (IsDeleted) throw new DomainException("refueling.trashedCannotEdit", "A log in the trash cannot be edited; restore it first.");
        Apply(date, volume, isFullTank, note);
        OdometerReading.Update(date, odometer);
        Cost.Update(date, totalCost, currency);
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        if (IsDeleted) throw new DomainException("refueling.alreadyTrashed", "This log is already in the trash.");
        DeletedAt = now;
        OdometerReading.MarkDeleted(now);
        Cost.MarkDeleted(now);
    }

    public void Restore()
    {
        if (!IsDeleted) throw new DomainException("refueling.notTrashed", "This log is not in the trash.");
        DeletedAt = null;
        OdometerReading.Restore();
        Cost.Restore();
    }

    private void Apply(DateOnly date, decimal volume, bool isFullTank, string? note)
    {
        if (volume <= 0) throw new DomainException("refueling.volumePositive", "The volume must be greater than zero.");
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
            throw new DomainException("refueling.noteTooLong", $"The note can be at most {MaxNoteLength} characters.", new { Max = MaxNoteLength });

        Date = date;
        Volume = volume;
        IsFullTank = isFullTank;
        Note = trimmed;
    }
}
