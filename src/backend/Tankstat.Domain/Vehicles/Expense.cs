using Tankstat.Domain.Access;
using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;

namespace Tankstat.Domain.Vehicles;

/// <summary>
/// Money spent on a vehicle that is not a fill-up: service, insurance, parking, tolls, ... Like a refuelling it does not carry
/// an amount or odometer number of its own but owns a <see cref="Cost"/> and, when the odometer was noted, an
/// <see cref="OdometerReading"/> (they are trashed, restored and deleted with it). The category is free text, so whatever an
/// import brings along (or the user types) is kept as is.
/// </summary>
public sealed class Expense : IOwned, ISoftDeletable
{
    public const int MaxTitleLength = 120;
    public const int MaxCategoryLength = 60;
    public const int MaxNoteLength = 500;

    private Expense() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Always the owner of the vehicle, denormalized so access filtering works on this table directly.</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }

    /// <summary>Who logged it (the anonymous owner when authentication is off).</summary>
    public Guid CreatedById { get; private set; }
    public DateOnly Date { get; private set; }
    public string Title { get; private set; } = "";
    public string? Category { get; private set; }
    public string? Note { get; private set; }

    public Guid CostId { get; private set; }

    /// <summary>The linked cost; load it (Include) before reading <see cref="Amount"/> or <see cref="Currency"/>.</summary>
    public Cost Cost { get; private set; } = null!;
    public decimal Amount => Cost.Amount;
    public string Currency => Cost.Currency;

    public Guid? OdometerReadingId { get; private set; }

    /// <summary>The linked reading, if the odometer was noted; load it (Include) before reading <see cref="Odometer"/>.</summary>
    public OdometerReading? OdometerReading { get; private set; }

    /// <summary>The odometer value in the vehicle's distance unit, or null when it was not noted.</summary>
    public long? Odometer => OdometerReading?.Value;

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Expense Create(
        Guid ownerId, Guid createdById, Guid vehicleId, DateOnly date, string title, string? category, Cost cost, OdometerReading? reading, string? note = null)
    {
        if (cost.VehicleId != vehicleId || (reading is not null && reading.VehicleId != vehicleId))
            throw new DomainException("expense.wrongVehicle", "The odometer reading and the cost must belong to the same vehicle as the expense.");

        var expense = new Expense
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, CreatedById = createdById, VehicleId = vehicleId,
            CostId = cost.Id, Cost = cost, OdometerReadingId = reading?.Id, OdometerReading = reading,
        };
        expense.Apply(date, title, category, note);
        cost.Update(date, cost.Amount, cost.Currency);
        reading?.Update(date, reading.Value);
        return expense;
    }

    /// <summary>
    /// Changes the expense. <paramref name="odometer"/> null means "not noted": an existing reading is detached and returned so the
    /// caller can delete it; a reading that has to be created for a newly noted odometer is returned through <paramref name="created"/>.
    /// </summary>
    public OdometerReading? Update(
        DateOnly date, string title, string? category, decimal amount, string currency, long? odometer, string? note, out OdometerReading? created)
    {
        if (IsDeleted) throw new DomainException("expense.trashedCannotEdit", "An expense in the trash cannot be edited; restore it first.");
        Apply(date, title, category, note);
        Cost.Update(date, amount, currency);

        created = null;
        OdometerReading? detached = null;
        if (odometer is null)
        {
            detached = OdometerReading;
            OdometerReading = null;
            OdometerReadingId = null;
        }
        else if (OdometerReading is null)
        {
            created = OdometerReading.Create(OwnerId, VehicleId, date, odometer.Value);
            OdometerReading = created;
            OdometerReadingId = created.Id;
        }
        else
        {
            OdometerReading.Update(date, odometer.Value);
        }
        return detached;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        if (IsDeleted) throw new DomainException("expense.alreadyTrashed", "This expense is already in the trash.");
        DeletedAt = now;
        OdometerReading?.MarkDeleted(now);
        Cost.MarkDeleted(now);
    }

    public void Restore()
    {
        if (!IsDeleted) throw new DomainException("expense.notTrashed", "This expense is not in the trash.");
        DeletedAt = null;
        OdometerReading?.Restore();
        Cost.Restore();
    }

    private void Apply(DateOnly date, string title, string? category, string? note)
    {
        var trimmedTitle = title?.Trim() ?? "";
        if (trimmedTitle.Length == 0) throw new DomainException("expense.titleRequired", "The expense needs a title.");
        if (trimmedTitle.Length > MaxTitleLength)
            throw new DomainException("expense.titleTooLong", $"The title can be at most {MaxTitleLength} characters.", new { Max = MaxTitleLength });
        var trimmedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (trimmedCategory is { Length: > MaxCategoryLength })
            throw new DomainException("expense.categoryTooLong", $"The category can be at most {MaxCategoryLength} characters.", new { Max = MaxCategoryLength });
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > MaxNoteLength })
            throw new DomainException("expense.noteTooLong", $"The note can be at most {MaxNoteLength} characters.", new { Max = MaxNoteLength });

        Date = date;
        Title = trimmedTitle;
        Category = trimmedCategory;
        Note = trimmedNote;
    }
}
