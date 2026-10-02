using Tankstat.Domain.Access;
using Tankstat.Domain.Odometers;

namespace Tankstat.Domain.Recurring;

/// <summary>What makes a recurring expense due again: the calendar, the odometer, or whichever is reached first.</summary>
public enum RecurrenceKind
{
    Time,
    Odometer,
    Combined,
}

/// <summary>
/// An expense that comes back again and again on a vehicle (insurance every 12 months, an oil change every 15,000 km or 12 months,
/// whichever comes first). It is a schedule, not a cost: the item remembers when it was last done (the baseline) and
/// <see cref="RecurrenceCalculator"/> works out when it is due next. Marking it done moves the baseline; the money itself is an
/// <c>Expense</c> logged at that moment. The baseline odometer is the number at that moment, not a reading of its own, so it does not
/// take part in the neighbour checks of real readings.
/// </summary>
public sealed class RecurringExpense : IOwned
{
    public const int MaxTitleLength = 120;
    public const int MaxCategoryLength = 60;
    public const int MaxNoteLength = 500;
    public const int MaxIntervalMonths = 600;
    public const int MaxWarnDays = 365;
    public const int DefaultWarnDays = 30;
    public const long DefaultWarnDistance = 500;

    private RecurringExpense() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Always the owner of the vehicle, denormalized so access filtering works on this table directly.</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid CreatedById { get; private set; }

    /// <summary>When it was added (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }
    public string Title { get; private set; } = "";
    public string? Category { get; private set; }
    public string? Note { get; private set; }
    public RecurrenceKind Kind { get; private set; }

    /// <summary>Months between two times; set for <see cref="RecurrenceKind.Time"/> and <see cref="RecurrenceKind.Combined"/>.</summary>
    public int? IntervalMonths { get; private set; }

    /// <summary>Distance (in the vehicle's unit) between two times; set for <see cref="RecurrenceKind.Odometer"/> and <see cref="RecurrenceKind.Combined"/>.</summary>
    public long? IntervalDistance { get; private set; }

    /// <summary>When it was last done (or the day the schedule starts counting from).</summary>
    public DateOnly LastDoneDate { get; private set; }

    /// <summary>The odometer then; required when the kind uses distance.</summary>
    public long? LastDoneOdometer { get; private set; }

    /// <summary>How many days before the due date it counts as "due soon".</summary>
    public int WarnDays { get; private set; }

    /// <summary>How much distance before the due odometer it counts as "due soon".</summary>
    public long WarnDistance { get; private set; }

    public bool UsesTime => Kind is RecurrenceKind.Time or RecurrenceKind.Combined;
    public bool UsesDistance => Kind is RecurrenceKind.Odometer or RecurrenceKind.Combined;

    public static RecurringExpense Create(
        Guid ownerId, Guid createdById, Guid vehicleId, string title, string? category, string? note, RecurrenceKind kind,
        int? intervalMonths, long? intervalDistance, DateOnly lastDoneDate, long? lastDoneOdometer, int warnDays, long warnDistance, DateTimeOffset createdAt)
    {
        var item = new RecurringExpense { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedById = createdById, VehicleId = vehicleId, CreatedAt = createdAt };
        item.Apply(title, category, note, kind, intervalMonths, intervalDistance, lastDoneDate, lastDoneOdometer, warnDays, warnDistance);
        return item;
    }

    public void Update(
        string title, string? category, string? note, RecurrenceKind kind, int? intervalMonths, long? intervalDistance,
        DateOnly lastDoneDate, long? lastDoneOdometer, int warnDays, long warnDistance) =>
        Apply(title, category, note, kind, intervalMonths, intervalDistance, lastDoneDate, lastDoneOdometer, warnDays, warnDistance);

    /// <summary>Starts the next interval from the day (and odometer) it was done.</summary>
    public void MarkDone(DateOnly date, long? odometer)
    {
        CheckDone(date, odometer);
        LastDoneDate = date;
        LastDoneOdometer = odometer ?? LastDoneOdometer;
    }

    /// <summary>Throws when <see cref="MarkDone"/> would refuse; changes nothing, so it can run before the expense is logged.</summary>
    public void CheckDone(DateOnly date, long? odometer)
    {
        if (date < LastDoneDate)
            throw new DomainException("recurring.doneBeforeLast", $"It cannot be done before {LastDoneDate:yyyy-MM-dd}, when it was last done.", new { Date = LastDoneDate.ToString("yyyy-MM-dd") });
        if (UsesDistance && odometer is null)
            throw new DomainException("recurring.odometerRequired", "The odometer is needed to start the next interval.");
        if (odometer is { } value)
        {
            OdometerValue.From(value);
            if (LastDoneOdometer is { } last && value < last)
                throw new DomainException("recurring.odometerBelowLast", $"The odometer cannot be lower than {last}, where it was last done.", new { Last = last });
        }
    }

    private void Apply(
        string title, string? category, string? note, RecurrenceKind kind, int? intervalMonths, long? intervalDistance,
        DateOnly lastDoneDate, long? lastDoneOdometer, int warnDays, long warnDistance)
    {
        var trimmedTitle = title?.Trim() ?? "";
        if (trimmedTitle.Length == 0) throw new DomainException("recurring.titleRequired", "The recurring expense needs a title.");
        if (trimmedTitle.Length > MaxTitleLength)
            throw new DomainException("recurring.titleTooLong", $"The title can be at most {MaxTitleLength} characters.", new { Max = MaxTitleLength });
        var trimmedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (trimmedCategory is { Length: > MaxCategoryLength })
            throw new DomainException("recurring.categoryTooLong", $"The category can be at most {MaxCategoryLength} characters.", new { Max = MaxCategoryLength });
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > MaxNoteLength })
            throw new DomainException("recurring.noteTooLong", $"The note can be at most {MaxNoteLength} characters.", new { Max = MaxNoteLength });
        if (!Enum.IsDefined(kind)) throw new DomainException("recurring.unknownKind", $"Unknown kind '{kind}'.", new { Value = kind.ToString() });

        var usesTime = kind is RecurrenceKind.Time or RecurrenceKind.Combined;
        var usesDistance = kind is RecurrenceKind.Odometer or RecurrenceKind.Combined;
        if (usesTime && intervalMonths is not (>= 1 and <= MaxIntervalMonths))
            throw new DomainException("recurring.monthsInvalid", $"The time interval must be between 1 and {MaxIntervalMonths} months.", new { Max = MaxIntervalMonths });
        if (usesDistance && intervalDistance is not >= 1)
            throw new DomainException("recurring.distanceInvalid", "The distance interval must be at least 1.");
        if (usesDistance && lastDoneOdometer is null)
            throw new DomainException("recurring.odometerRequired", "The odometer is needed to start the next interval.");
        if (lastDoneOdometer is { } odometer) OdometerValue.From(odometer);
        if (warnDays is < 0 or > MaxWarnDays)
            throw new DomainException("recurring.warnDaysInvalid", $"The warning time must be between 0 and {MaxWarnDays} days.", new { Max = MaxWarnDays });
        if (warnDistance < 0) throw new DomainException("recurring.warnDistanceInvalid", "The warning distance cannot be negative.");

        Title = trimmedTitle;
        Category = trimmedCategory;
        Note = trimmedNote;
        Kind = kind;
        IntervalMonths = usesTime ? intervalMonths : null; // what the kind does not use is dropped
        IntervalDistance = usesDistance ? intervalDistance : null;
        LastDoneDate = lastDoneDate;
        LastDoneOdometer = lastDoneOdometer;
        WarnDays = warnDays;
        WarnDistance = warnDistance;
    }
}
