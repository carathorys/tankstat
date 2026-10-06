namespace Tankstat.Domain.Settings;

/// <summary>
/// Where one vehicle stands in one user's home list (from 0). It goes with its vehicle (the table cascades) and with its user
/// (<c>UserDataRepository</c>); it is never handed to another user.
/// </summary>
public sealed class VehicleOrder
{
    private VehicleOrder() { } // EF Core

    public Guid UserId { get; private set; }
    public Guid VehicleId { get; private set; }
    public int Position { get; private set; }

    public static VehicleOrder Create(Guid userId, Guid vehicleId, int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position); // positions are worked out by the service, never typed by a user
        return new VehicleOrder { UserId = userId, VehicleId = vehicleId, Position = position };
    }
}
