namespace Tankstat.Domain.Settings;

/// <summary>
/// What a user's devices download for offline use: one row per user (the anonymous user's when authentication is off), no foreign key to
/// the user, like <see cref="UiSettings"/>. The vehicles that differ from the default are <see cref="OfflineVehicleSetting"/> rows.
/// </summary>
public sealed class OfflineSettings
{
    private OfflineSettings() { } // EF Core

    public Guid UserId { get; private set; }

    /// <summary>An <see cref="OfflineWindow"/> rule.</summary>
    public string DefaultWindow { get; private set; } = OfflineWindow.Default;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static OfflineSettings Create(Guid userId, string defaultWindow, DateTimeOffset now) =>
        new() { UserId = userId, DefaultWindow = OfflineWindow.Require(defaultWindow), UpdatedAt = now };

    public void SetDefault(string window, DateTimeOffset now)
    {
        DefaultWindow = OfflineWindow.Require(window);
        UpdatedAt = now;
    }
}

/// <summary>One vehicle's window for one user, when it differs from their default. It goes with its vehicle (the table cascades) and with its user.</summary>
public sealed class OfflineVehicleSetting
{
    private OfflineVehicleSetting() { } // EF Core

    public Guid UserId { get; private set; }
    public Guid VehicleId { get; private set; }

    /// <summary>An <see cref="OfflineWindow"/> rule.</summary>
    public string Window { get; private set; } = OfflineWindow.Default;

    public static OfflineVehicleSetting Create(Guid userId, Guid vehicleId, string window) =>
        new() { UserId = userId, VehicleId = vehicleId, Window = OfflineWindow.Require(window) };
}
