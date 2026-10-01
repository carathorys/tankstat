using Tankstat.Application.Vehicles;

namespace Tankstat.Application.Refuelings;

public enum RefuelingSortField
{
    Date,
    Volume,
    TotalCost,
    Odometer,
    PricePerUnit,
    Consumption,
    CreatedBy,
    Vehicle,
    DeletedAt,
}

/// <summary>Ordering and paging of a list of refuelings (done in the database, never in the client).</summary>
public sealed record RefuelingQuery(
    RefuelingSortField SortBy = RefuelingSortField.Date,
    SortDirection Direction = SortDirection.Desc,
    int Skip = 0,
    int Take = RefuelingQuery.DefaultTake)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public RefuelingQuery Normalized() => this with { Skip = Math.Max(0, Skip), Take = Math.Clamp(Take, 1, MaxTake) };
}
