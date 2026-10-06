using System.Text.RegularExpressions;

namespace Tankstat.Domain.Settings;

/// <summary>What a user wants of one grid: column order, hidden columns, page size and sort.</summary>
public sealed record GridSettingsValues(IReadOnlyList<string> Order, IReadOnlyList<string> Hidden, int PageSize, string SortColumn, SortDirection SortDirection);

/// <summary>
/// How one user wants one grid, on every device. Ids are checked for shape and count only: the UI owns their meaning and drops the ones
/// its current columns do not know (useGridSettings.merge).
/// </summary>
public sealed partial class GridSettings
{
    public const int MaxGridIdLength = 40;
    public const int MaxColumnIdLength = 40;
    public const int MaxColumns = 50;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 500;

    private GridSettings() { } // EF Core

    public Guid UserId { get; private set; }
    public string GridId { get; private set; } = "";
    public IReadOnlyList<string> Order { get; private set; } = [];
    public IReadOnlyList<string> Hidden { get; private set; } = [];
    public int PageSize { get; private set; }
    public string SortColumn { get; private set; } = "";
    public SortDirection SortDirection { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The trimmed grid id (lower-case letters, digits and dashes, starting with a letter), or <c>settings.gridIdInvalid</c>.</summary>
    public static string CheckGridId(string? gridId)
    {
        var id = gridId?.Trim() ?? "";
        if (id.Length == 0 || id.Length > MaxGridIdLength || !GridIdPattern().IsMatch(id))
            throw new DomainException("settings.gridIdInvalid", "The grid name is not valid.");
        return id;
    }

    public static GridSettings Create(Guid userId, string gridId, GridSettingsValues values, DateTimeOffset now)
    {
        var grid = new GridSettings { UserId = userId, GridId = CheckGridId(gridId) };
        grid.Update(values, now);
        return grid;
    }

    public void Update(GridSettingsValues values, DateTimeOffset now)
    {
        if (values.PageSize is < MinPageSize or > MaxPageSize)
            throw new DomainException("settings.pageSizeInvalid", $"The page size must be between {MinPageSize} and {MaxPageSize}.", new { Min = MinPageSize, Max = MaxPageSize });
        if (!Enum.IsDefined(values.SortDirection)) throw new DomainException("settings.sortDirectionInvalid", "Unknown sort direction.");
        Order = CheckColumns(values.Order);
        Hidden = CheckColumns(values.Hidden);
        SortColumn = CheckColumn(values.SortColumn);
        PageSize = values.PageSize;
        SortDirection = values.SortDirection;
        UpdatedAt = now;
    }

    /// <summary>Every id in shape, duplicates dropped (the first wins), at most <see cref="MaxColumns"/>.</summary>
    private static IReadOnlyList<string> CheckColumns(IReadOnlyList<string> ids)
    {
        var distinct = ids.Select(CheckColumn).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > MaxColumns)
            throw new DomainException("settings.tooManyColumns", $"A grid can remember at most {MaxColumns} columns.", new { Max = MaxColumns });
        return distinct;
    }

    private static string CheckColumn(string? id) =>
        id is not null && id.Length <= MaxColumnIdLength && ColumnIdPattern().IsMatch(id)
            ? id
            : throw new DomainException("settings.columnIdInvalid", "A column name is not valid.");

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex GridIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex ColumnIdPattern();
}
