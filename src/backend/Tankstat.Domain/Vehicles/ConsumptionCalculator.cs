namespace Tankstat.Domain.Vehicles;

/// <summary>
/// Fuel consumption between full fill-ups. A full fill-up measures how much fuel was used since the previous full one, so its
/// consumption is all the fuel added since then (the previous full one is not counted; partial fill-ups in between are) divided by
/// the distance driven, times 100. Partial fill-ups themselves have no consumption, and neither has the first full fill-up.
/// The result depends on neighbouring logs, so it is recalculated for the whole vehicle whenever one of its logs changes.
/// A log whose odometer or volume is not known yet (its photo is still being read) makes the interval it falls in unknown, and a full
/// fill-up without an odometer cannot start the next one.
/// </summary>
public static class ConsumptionCalculator
{
    /// <summary>
    /// Sets the consumption of every log of one vehicle (live logs only, odometer readings loaded) and returns the logs whose
    /// stored value changed, so only those need saving. Logs are ordered by date, then odometer.
    /// </summary>
    public static IReadOnlyList<Refueling> Apply(IEnumerable<Refueling> logs)
    {
        var changed = new List<Refueling>();
        long? previousFull = null; // the odometer of the previous full fill-up, when it is known
        decimal? fuel = 0; // null once a fill-up of unknown volume is in the interval

        foreach (var log in logs.OrderBy(l => l.Date).ThenBy(l => l.Odometer ?? long.MaxValue).ThenBy(l => l.Id))
        {
            decimal? value = null;
            if (log.IsFullTank)
            {
                fuel += log.Volume;
                var distance = previousFull is null ? null : log.Odometer - previousFull;
                if (distance > 0 && fuel is { } used) value = used / distance.Value * 100;
                previousFull = log.Odometer;
                fuel = 0;
            }
            else if (previousFull is not null)
            {
                fuel += log.Volume;
            }

            var before = log.Consumption;
            log.SetConsumption(value);
            if (log.Consumption != before) changed.Add(log);
        }
        return changed;
    }
}
