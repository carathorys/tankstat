namespace Tankstat.Domain.Odometers;

/// <summary>
/// An odometer reading: a non-negative whole number with no upper limit. The unit (kilometres or miles) belongs to the
/// vehicle (<see cref="DistanceUnit"/>), not to the number. The one place that says what a valid reading is, shared by every
/// entity that records one (refuelings today, inspections and others later).
/// </summary>
public readonly record struct OdometerValue
{
    public long Value { get; }

    private OdometerValue(long value) => Value = value;

    public static OdometerValue From(long value) =>
        value < 0 ? throw new DomainException("odometer.negative", "The odometer cannot be negative.") : new OdometerValue(value);

    public override string ToString() => Value.ToString();
}
