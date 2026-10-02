using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Vehicles;
using Tankstat.Domain.Access;
using Tankstat.Domain.Vehicles;
using Tankstat.Domain.Photos;

namespace Tankstat.Application.Photos;

/// <summary>A live log with the vehicle it belongs to and what the current user may do with the vehicle's logs.</summary>
public sealed record LogContext(Vehicle Vehicle, LogType LogType, Guid LogId, AccessLevel Level);

/// <summary>
/// Who may see or change the photos of a log: exactly those who may see or change the log itself, i.e. the access rules of the
/// vehicle's logs. Photos of logs in the trash (or whose vehicle is) are hidden like the logs.
/// </summary>
public sealed class LogPhotoAccess(
    AccessService access, IVehicleRepository vehicles, IExpenseRepository expenses, IRefuelingRepository refuelings, ILogPhotoRepository photos)
{
    /// <summary>Null when the log does not exist, is trashed, or the user may not see it (existence is not revealed).</summary>
    public async Task<LogContext?> FindAsync(LogType logType, Guid logId, CancellationToken ct)
    {
        var vehicleId = logType switch
        {
            LogType.Expense => (await expenses.FindAsync(logId, ct))?.VehicleId,
            LogType.Refueling => (await refuelings.FindAsync(logId, ct))?.VehicleId,
            _ => null,
        };
        if (vehicleId is null || await vehicles.FindAsync(vehicleId.Value, ct) is not { } vehicle) return null;

        var level = await access.LogLevelAsync(vehicle, ct);
        return level >= AccessLevel.View ? new LogContext(vehicle, logType, logId, level) : null;
    }

    /// <summary>The log for changing its photos: unseen means not found, view-only is forbidden.</summary>
    public async Task<LogContext> EditableAsync(LogType logType, Guid logId, CancellationToken ct)
    {
        var context = await FindAsync(logType, logId, ct)
                      ?? throw new NotFoundException(logType == LogType.Expense ? "expense.notFound" : "refueling.notFound",
                          $"The log {logId} does not exist.", new { Id = logId });
        if (context.Level < AccessLevel.Edit) throw new ForbiddenException("vehicle.viewOnly", "You may only view this vehicle.");
        return context;
    }

    /// <summary>Whether the image is a photo of a log the current user may see.</summary>
    public async Task<bool> CanSeeImageAsync(Guid imageId, CancellationToken ct) =>
        await photos.FindByImageAsync(imageId, ct) is { } photo && await FindAsync(photo.LogType, photo.LogId, ct) is not null;
}
