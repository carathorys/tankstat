using Tankstat.Application.Settings;
using Tankstat.Domain;
using Tankstat.Domain.Settings;

namespace Tankstat.Api.GraphQL;

/// <summary>How one user wants one grid: column order, hidden columns, page size and sort. The UI drops ids its current columns do not know.</summary>
public sealed record GridSettingsInfo(string GridId, IReadOnlyList<string> Order, IReadOnlyList<string> Hidden, int PageSize, string SortColumn, SortDirection SortDirection)
{
    public static GridSettingsInfo From(GridSettings g) => new(g.GridId, g.Order, g.Hidden, g.PageSize, g.SortColumn, g.SortDirection);
}

/// <summary>The current user's UI settings; a null value means nothing was chosen yet (the browser's own default applies). The grids are loaded only when selected.</summary>
public sealed record UiSettingsInfo(bool? NavOpen, string? Language)
{
    public static UiSettingsInfo From(UiSettingsView v) => new(v.NavOpen, v.Language);
}

[ExtendObjectType<UiSettingsInfo>]
public sealed class UiSettingsInfoExtensions
{
    /// <summary>How the user wants each grid they changed, by grid id.</summary>
    public async Task<IReadOnlyList<GridSettingsInfo>> GetGrids([Service] UiSettingsService settings, CancellationToken ct) =>
        (await settings.ListGridsAsync(ct)).Select(GridSettingsInfo.From).ToList();
}

/// <param name="NavOpen">Omit to leave it as it is.</param>
/// <param name="Language">Omit to leave it as it is.</param>
/// <param name="ClearLanguage">Forget the saved language (the browser's choice applies again); wins over a language given at the same time.</param>
public sealed record UpdateUiSettingsInput(bool? NavOpen, string? Language, bool? ClearLanguage);

public sealed record GridSettingsInput(string GridId, IReadOnlyList<string> Order, IReadOnlyList<string> Hidden, int PageSize, string SortColumn, SortDirection SortDirection);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class UiSettingsQueries
{
    /// <summary>What the UI remembers for the current user on every device (with authentication off: for everyone, the anonymous user's).</summary>
    public async Task<UiSettingsInfo> GetUiSettings([Service] UiSettingsService settings, CancellationToken ct) =>
        UiSettingsInfo.From(await settings.GetAsync(ct));
}

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class UiSettingsMutations
{
    public async Task<UiSettingsInfo> UpdateUiSettings(UpdateUiSettingsInput input, [Service] UiSettingsService settings, CancellationToken ct) =>
        UiSettingsInfo.From(await settings.UpdateAsync(new UiSettingsChange(input.NavOpen, input.Language, input.ClearLanguage ?? false), ct));

    public async Task<GridSettingsInfo> SaveGridSettings(GridSettingsInput input, [Service] UiSettingsService settings, CancellationToken ct) =>
        GridSettingsInfo.From(await settings.SaveGridAsync(input.GridId, new GridSettingsValues(input.Order, input.Hidden, input.PageSize, input.SortColumn, input.SortDirection), ct));

    /// <summary>Forgets what was saved for a grid; true when there was something to forget.</summary>
    public Task<bool> ResetGridSettings(string gridId, [Service] UiSettingsService settings, CancellationToken ct) => settings.ResetGridAsync(gridId, ct);

    /// <summary>The user's own order of their vehicles on the home page, as a whole: the ones listed come first in this order, the rest follow by name.</summary>
    public async Task<bool> SetVehicleOrder(IReadOnlyList<Guid> vehicleIds, [Service] VehicleOrderService order, CancellationToken ct)
    {
        await order.SetAsync(vehicleIds, ct);
        return true;
    }
}
