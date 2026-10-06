using Tankstat.Domain;
using Tankstat.Domain.Odometers;

namespace Tankstat.Application.Odometers;

/// <summary>
/// The rules every odometer reading follows, whatever entity records it (refuelings today, inspections later): a valid
/// value, and consistent with the other readings of the same vehicle over time.
/// </summary>
public sealed class OdometerService(IOdometerReadingRepository readings)
{
    /// <summary>
    /// Throws unless <paramref name="value"/> is valid and fits between the readings recorded before and after <paramref name="date"/>
    /// (equal values are fine). Pass <paramref name="exceptReadingId"/> when editing so the reading does not compare against itself.
    /// </summary>
    public async Task ValidateAsync(Guid vehicleId, DateOnly date, long value, Guid? exceptReadingId, CancellationToken ct)
    {
        value = OdometerValue.From(value).Value;

        var previous = await readings.PreviousAsync(vehicleId, date, exceptReadingId, ct);
        if (previous is not null && value < previous.Value)
            throw new DomainException("odometer.belowPrevious", $"The odometer cannot be lower than {previous.Value} on {previous.Date:yyyy-MM-dd}.",
                new { Previous = previous.Value, Date = previous.Date.ToString("yyyy-MM-dd") });

        var next = await readings.NextAsync(vehicleId, date, exceptReadingId, ct);
        if (next is not null && value > next.Value)
            throw new DomainException("odometer.aboveNext", $"The odometer cannot be higher than {next.Value} on {next.Date:yyyy-MM-dd}.",
                new { Next = next.Value, Date = next.Date.ToString("yyyy-MM-dd") });
    }

    /// <summary>The most recent reading of any kind, to suggest as the starting value of a new one.</summary>
    public Task<OdometerReading?> LatestAsync(Guid vehicleId, CancellationToken ct) => readings.LatestAsync(vehicleId, ct);

    /// <summary>The vehicle's latest reading on or before <paramref name="date"/>, leaving one reading (a log's own) out.</summary>
    public Task<OdometerReading?> PreviousAsync(Guid vehicleId, DateOnly date, Guid? exceptReadingId, CancellationToken ct) => readings.PreviousAsync(vehicleId, date, exceptReadingId, ct);

    /// <summary>The most recent reading of each vehicle, in one query; vehicles without a reading are missing.</summary>
    public Task<IReadOnlyDictionary<Guid, OdometerReading>> LatestForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct) =>
        readings.LatestForVehiclesAsync(vehicleIds, ct);

    public Task<bool> HasReadingsAsync(Guid vehicleId, CancellationToken ct) => readings.AnyAsync(vehicleId, ct);
}
