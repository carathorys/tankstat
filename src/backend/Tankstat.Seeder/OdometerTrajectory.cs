using Tankstat.Domain.Vehicles;

namespace Tankstat.Seeder;

/// <summary>
/// Where a vehicle's odometer stood on any day, read from its fuel log: a refuelling's own value on its day, a straight line between two
/// of them, and the log's own pace (distance per day) before the first one, floored at 0. It never goes down as the date goes up, so
/// readings taken from it (an expense's) always fit between the readings of the days before and after them, as the app requires.
/// </summary>
public sealed class OdometerTrajectory
{
    /// <summary>How far back <see cref="DateAt"/> looks for a day.</summary>
    private const int SearchYears = 30;

    private readonly (int Day, long Value)[] _points;
    private readonly double _perDay;

    private OdometerTrajectory((int Day, long Value)[] points, double perDay) => (_points, _perDay) = (points, perDay);

    /// <summary>The log's trajectory, or null for a vehicle without a log (nothing says where its odometer stood).</summary>
    /// <param name="log">Refuellings in date order, at most one a day, the odometer never going down (as the generator makes them).</param>
    /// <param name="defaultPerDay">The pace (in the vehicle's unit) when one refuelling says nothing about it.</param>
    public static OdometerTrajectory? Of(IReadOnlyList<Refueling> log, double defaultPerDay)
    {
        if (log.Count == 0) return null;
        var points = log.Select(r => (Day: r.Date.DayNumber, Value: r.Odometer!.Value)).ToArray(); // every generated refuelling has a reading
        var days = points[^1].Day - points[0].Day;
        return new OdometerTrajectory(points, days > 0 ? (double)(points[^1].Value - points[0].Value) / days : defaultPerDay);
    }

    public DateOnly First => DateOnly.FromDayNumber(_points[0].Day);
    public DateOnly Last => DateOnly.FromDayNumber(_points[^1].Day);

    /// <summary>The latest reading of the log.</summary>
    public long Latest => _points[^1].Value;

    public long At(DateOnly date)
    {
        var day = date.DayNumber;
        var (firstDay, firstValue) = _points[0];
        if (day <= firstDay) return Math.Max(0, firstValue - (long)Math.Ceiling((firstDay - day) * _perDay));
        var (lastDay, lastValue) = _points[^1];
        if (day >= lastDay) return lastValue + (long)Math.Floor((day - lastDay) * _perDay);

        // The last refuelling on or before the day and the next one (binary search: the points are in date order).
        int low = 0, high = _points.Length - 1;
        while (high - low > 1)
        {
            var mid = low + (high - low) / 2;
            if (_points[mid].Day <= day) low = mid;
            else high = mid;
        }
        var (fromDay, fromValue) = _points[low];
        var (toDay, toValue) = _points[high];
        return fromValue + (toValue - fromValue) * (day - fromDay) / (toDay - fromDay);
    }

    /// <summary>
    /// The last day, at most the log's last, on which the odometer had not passed <paramref name="value"/>: a reading of that value on
    /// that day is not below any reading of the days before nor above any of the days after. When it had passed it already on the first
    /// day searched (<see cref="SearchYears"/> back), that day, where such a reading still fits (it is below every later one).
    /// </summary>
    public DateOnly DateAt(long value)
    {
        int low = Last.AddYears(-SearchYears).DayNumber, high = _points[^1].Day;
        if (At(DateOnly.FromDayNumber(low)) > value) return DateOnly.FromDayNumber(low);
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (At(DateOnly.FromDayNumber(mid)) <= value) low = mid;
            else high = mid - 1;
        }
        return DateOnly.FromDayNumber(low);
    }
}
