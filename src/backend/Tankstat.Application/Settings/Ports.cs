using Tankstat.Domain.Settings;

namespace Tankstat.Application.Settings;

public interface IUiSettingsRepository
{
    Task<UiSettings?> FindAsync(Guid userId, CancellationToken ct);

    /// <summary>Adds the row or replaces it: the key is the user id, never generated.</summary>
    Task SaveAsync(UiSettings settings, CancellationToken ct);

    /// <summary>The user's grids, by grid id.</summary>
    Task<IReadOnlyList<GridSettings>> ListGridsAsync(Guid userId, CancellationToken ct);

    Task<GridSettings?> FindGridAsync(Guid userId, string gridId, CancellationToken ct);

    /// <summary>Adds the row or replaces it.</summary>
    Task SaveGridAsync(GridSettings grid, CancellationToken ct);

    /// <summary>True when there was a row to remove.</summary>
    Task<bool> RemoveGridAsync(Guid userId, string gridId, CancellationToken ct);
}

public interface IVehicleOrderRepository
{
    /// <summary>All of the user's rows go and these come, in one transaction. The arrangement is only ever read as part of the vehicle list.</summary>
    Task ReplaceAsync(Guid userId, IReadOnlyList<VehicleOrder> order, CancellationToken ct);
}

public interface IOfflineSettingsRepository
{
    Task<OfflineSettings?> FindAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<OfflineVehicleSetting>> ListVehiclesAsync(Guid userId, CancellationToken ct);

    /// <summary>The user's row is added or replaced and all their vehicle rows go and these come, in one transaction.</summary>
    Task ReplaceAsync(OfflineSettings settings, IReadOnlyList<OfflineVehicleSetting> vehicles, CancellationToken ct);
}
