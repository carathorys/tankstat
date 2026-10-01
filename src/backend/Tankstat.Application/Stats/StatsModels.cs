using Tankstat.Domain.Charts;

namespace Tankstat.Application.Stats;

/// <summary>One fill-up, reduced to what statistics need.</summary>
public sealed record FuelPoint(DateOnly Date, decimal Volume, decimal Amount, string Currency, long Odometer, bool IsFull, decimal? Consumption);

public sealed record ExpensePoint(DateOnly Date, string? Category, decimal Amount, string Currency);

public sealed record OdometerPoint(DateOnly Date, long Value);

/// <summary>Everything a vehicle's statistics are calculated from (live records only).</summary>
public sealed record StatsData(IReadOnlyList<FuelPoint> Fuel, IReadOnlyList<ExpensePoint> Expenses, IReadOnlyList<OdometerPoint> Readings);

public enum ChartUnit
{
    Currency,
    Volume,
    Distance,
    Consumption,
    Count,
}

/// <param name="Key">The period ("2026-09", "2026-Q3", "2026") or the expense category (empty for uncategorised).</param>
/// <param name="Value">Null where an average cannot be calculated (no data in that period).</param>
public sealed record ChartPoint(string Key, decimal? Value);

/// <param name="Kind">"total", "fuel" or "expenses" (translated by the client).</param>
/// <param name="Currency">The currency of a cost series; null for the others.</param>
public sealed record ChartSeries(string Kind, string? Currency, IReadOnlyList<ChartPoint> Points);

public sealed record ChartData(ChartUnit Unit, IReadOnlyList<ChartSeries> Series);

public sealed record MonthAmount(string Month, decimal Amount);

/// <summary>The key figures of a vehicle, for its card and the top of its dashboard.</summary>
public sealed record VehicleSummary(
    DateOnly? LastFillUpDate, long? LatestOdometer, decimal? AverageConsumption, string? Currency, decimal ThisMonthSpend, decimal LastMonthSpend,
    IReadOnlyList<MonthAmount> SpendTrend, int FillUpCount, int ExpenseCount);
