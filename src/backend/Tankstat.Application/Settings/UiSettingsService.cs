using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Domain.Settings;

namespace Tankstat.Application.Settings;

/// <summary>The current user's UI settings (the grids are listed separately); nulls mean nothing was chosen, so the browser's default applies.</summary>
public sealed record UiSettingsView(bool? NavOpen, string? Language, ColorMode? ColorMode)
{
    public static UiSettingsView Of(UiSettings? row) => new(row?.NavOpen, row?.Language, row?.ColorMode);
}

/// <summary>
/// A change to the settings: a null member leaves that setting as it is; <see cref="ClearLanguage"/> forgets the saved language and wins over
/// a <see cref="Language"/> given at the same time. A colour mode is never forgotten, only changed (System is the device's own choice).
/// </summary>
public sealed record UiSettingsChange(bool? NavOpen = null, string? Language = null, bool ClearLanguage = false, ColorMode? ColorMode = null)
{
    public bool IsEmpty => NavOpen is null && Language is null && !ClearLanguage && ColorMode is null;
}

/// <summary>
/// What the UI remembers for a user on every device (the sidebar, each grid, the language, the colour mode): stored per user, read by the
/// browser once per session. With authentication off everything belongs to the anonymous user, that is to everyone on the instance.
/// </summary>
public sealed class UiSettingsService(IUiSettingsRepository settings, AccessService access, TimeProvider clock, ILogger<UiSettingsService> logger)
{
    public async Task<UiSettingsView> GetAsync(CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        return UiSettingsView.Of(await settings.FindAsync(user.Id, ct));
    }

    /// <summary>The user's grids, by grid id (asked for separately: a changed sidebar does not need them).</summary>
    public async Task<IReadOnlyList<GridSettings>> ListGridsAsync(CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        return await settings.ListGridsAsync(user.Id, ct);
    }

    public async Task<UiSettingsView> UpdateAsync(UiSettingsChange change, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var now = clock.GetUtcNow();
        var row = await settings.FindAsync(user.Id, ct);
        if (change.IsEmpty) return UiSettingsView.Of(row); // nothing to save, no row for nothing
        row ??= UiSettings.Create(user.Id, now);
        if (change.NavOpen is { } open) row.SetNavOpen(open, now);
        if (change.ClearLanguage) row.SetLanguage(null, now);
        else if (change.Language is not null) row.SetLanguage(change.Language, now);
        if (change.ColorMode is { } mode) row.SetColorMode(mode, now);
        await settings.SaveAsync(row, ct);
        logger.LogDebug("User {UserId} changed their UI settings", user.Id);
        return UiSettingsView.Of(row);
    }

    public async Task<GridSettings> SaveGridAsync(string gridId, GridSettingsValues values, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var id = GridSettings.CheckGridId(gridId);
        var now = clock.GetUtcNow();
        var grid = await settings.FindGridAsync(user.Id, id, ct);
        if (grid is null) grid = GridSettings.Create(user.Id, id, values, now);
        else grid.Update(values, now);
        await settings.SaveGridAsync(grid, ct);
        logger.LogDebug("User {UserId} saved the settings of grid {GridId}", user.Id, id); // the id passed the pattern: letters, digits and dashes only
        return grid;
    }

    public async Task<bool> ResetGridAsync(string gridId, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var id = GridSettings.CheckGridId(gridId);
        var removed = await settings.RemoveGridAsync(user.Id, id, ct);
        if (removed) logger.LogDebug("User {UserId} reset grid {GridId}", user.Id, id);
        return removed;
    }
}
