using Tankstat.Application.Vehicles;
using Tankstat.Domain;

namespace Tankstat.Application.Expenses;

public enum ExpenseSortField
{
    Date,
    Title,
    Category,
    Amount,
    Odometer,
    CreatedBy,
    Vehicle,
    DeletedAt,
}

/// <summary>Ordering and paging of a list of expenses (done in the database, never in the client).</summary>
public sealed record ExpenseQuery(
    ExpenseSortField SortBy = ExpenseSortField.Date,
    SortDirection Direction = SortDirection.Desc,
    int Skip = 0,
    int Take = ExpenseQuery.DefaultTake)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public ExpenseQuery Normalized() => this with { Skip = Math.Max(0, Skip), Take = Math.Clamp(Take, 1, MaxTake) };
}
