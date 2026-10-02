namespace Tankstat.Application.Vehicles;

public enum SortDirection
{
    Asc,
    Desc,
}

public enum VehicleSortField
{
    Name,
    LicensePlate,
    FuelType,
    Owner,
    RefuelingCount,
    DeletedAt,
}

/// <summary>How a list of vehicles is ordered and paged. Sorting and paging happen in the database, never in the client.</summary>
public sealed record VehicleQuery(
    VehicleSortField SortBy = VehicleSortField.Name,
    SortDirection Direction = SortDirection.Asc,
    int Skip = 0,
    int Take = VehicleQuery.DefaultTake,
    string? Search = null)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;
    public const int MaxSearchLength = 100;

    /// <summary>Out-of-range paging values are clamped rather than rejected; a blank search means no search.</summary>
    public VehicleQuery Normalized() => this with
    {
        Skip = Math.Max(0, Skip),
        Take = Math.Clamp(Take, 1, MaxTake),
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()[..Math.Min(Search.Trim().Length, MaxSearchLength)],
    };
}
