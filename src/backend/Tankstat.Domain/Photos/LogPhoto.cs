using Tankstat.Domain.Access;

namespace Tankstat.Domain.Photos;

/// <summary>The kinds of logs a vehicle has; photos can be attached to each (a receipt, the pump display, ...).</summary>
public enum LogType
{
    Refueling,
    Expense,
}

/// <summary>
/// A photo attached to one log (refueling or expense) of a vehicle. The picture itself is a <c>StoredImage</c> kept in the vehicle's
/// upload folder. The log is not a foreign key (it can be either table), so photos are removed with their log in code; they belong
/// to the vehicle, so deleting the vehicle removes them in the database. Access follows the vehicle's log rules.
/// </summary>
public sealed class LogPhoto : IOwned
{
    public const int MaxPerLog = 10;

    private LogPhoto() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>The owner of the vehicle (a copy, like on the logs themselves).</summary>
    public Guid OwnerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public LogType LogType { get; private set; }
    public Guid LogId { get; private set; }
    public Guid ImageId { get; private set; }
    public Guid CreatedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static LogPhoto Create(Guid ownerId, Guid vehicleId, LogType logType, Guid logId, Guid imageId, Guid createdById, DateTimeOffset now)
    {
        if (!Enum.IsDefined(logType)) throw new DomainException("photo.unknownLogType", $"Unknown log type '{logType}'.", new { LogType = logType.ToString() });
        return new LogPhoto { Id = Guid.NewGuid(), OwnerId = ownerId, VehicleId = vehicleId, LogType = logType, LogId = logId, ImageId = imageId, CreatedById = createdById, CreatedAt = now };
    }
}
