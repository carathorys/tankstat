using Tankstat.Domain.Odometers;

namespace Tankstat.Domain.Measurements;

public enum VolumeUnit
{
    Liters,
    UsGallons,
    ImperialGallons,
}

/// <summary>
/// The units a vehicle's measurements are written in: distance (odometer) and fuel volume. Values are stored exactly as
/// entered and never converted, so the units of a vehicle cannot change once it has logs. (Money is not part of this:
/// every cost carries the currency it was paid in.)
/// </summary>
public sealed record MeasurementUnits
{
    private MeasurementUnits() { } // EF Core

    public DistanceUnit Distance { get; private set; }
    public VolumeUnit Volume { get; private set; }

    public static MeasurementUnits Create(DistanceUnit distance, VolumeUnit volume)
    {
        if (!Enum.IsDefined(distance)) throw new DomainException("vehicle.unknownUnit", $"Unknown distance unit '{distance}'.", new { Value = distance.ToString() });
        if (!Enum.IsDefined(volume)) throw new DomainException("vehicle.unknownUnit", $"Unknown volume unit '{volume}'.", new { Value = volume.ToString() });
        return new MeasurementUnits { Distance = distance, Volume = volume };
    }

    public static MeasurementUnits Metric => Create(DistanceUnit.Kilometers, VolumeUnit.Liters);
}
