using Tankstat.Domain.Measurements;
using Tankstat.Domain.Odometers;

namespace Tankstat.Application.Vehicles;

/// <summary>
/// Bound from the "Defaults" section (e.g. <c>Defaults__Currency=USD</c>): the units suggested when a vehicle is created.
/// Every vehicle keeps its own units; this only pre-fills the form for the country the instance is used in.
/// </summary>
public sealed class VehicleDefaultsOptions
{
    public const string SectionName = "Defaults";

    public DistanceUnit DistanceUnit { get; set; } = DistanceUnit.Kilometers;
    public VolumeUnit VolumeUnit { get; set; } = VolumeUnit.Liters;
    public string Currency { get; set; } = "EUR";
}
