using Microsoft.Extensions.Logging;
using Tankstat.Application.Access;
using Tankstat.Domain.Settings;

namespace Tankstat.Application.Settings;

/// <summary>The current user's UI settings as a whole; nulls mean nothing was chosen, so the browser's default applies.</summary>
public sealed record UiSettingsView(bool? NavOpen, string? Language, IReadOnlyList<GridSettings> Grids);

/// <summary>A change to the settings: a null member leaves that setting as it is; <see cref="ClearLanguage"/> forgets the saved language.</summary>
public sealed record UiSettingsChange(bool? NavOpen = null, string? Language = null, bool ClearLanguage = false);

/// <summary>
/// What the UI remembers for a user on every device (the sidebar, each grid, the language): stored per user, read by the browser once per
/// session. With authentication off everything belongs to the anonymous user, that is to everyone on the instance.
/// </summary>
public sealed class UiSettingsService(IUiSettingsRepository settings, AccessService access, TimeProvider clock, ILogger<UiSettingsService> logger)
{
    public async Task<UiSettingsView> GetAsync(CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var row = await settings.FindAsync(user.Id, ct);
        return new UiSettingsView(row?.NavOpen, row?.Language, await settings.ListGridsAsync(user.Id, ct));
    }

    public async Task<UiSettingsView> UpdateAsync(UiSettingsChange change, CancellationToken ct)
    {
        var user = await access.RequirePrincipalAsync(ct);
        var now = clock.GetUtcNow();
        var row = await settings.FindAsync(user.Id, ct) ?? UiSettings.Create(user.Id, now);
        if (change.NavOpen is { } open) row.SetNavOpen(open, now);
        if (change.ClearLanguage) row.SetLanguage(null, now);
        else if (change.Language is not null) row.SetLanguage(change.Language, now);
        await settings.SaveAsync(row, ct);
        logger.LogDebug("User {UserId} changed their UI settings", user.Id);
        return new UiSettingsView(row.NavOpen, row.Language, await settings.ListGridsAsync(user.Id, ct));
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
