using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Refuelings;
using Tankstat.Domain.Photos;

namespace Tankstat.Application.Photos;

/// <summary>
/// Who may see or change the photos of a log: exactly those who may see or change the log itself, i.e. the access rules of the
/// vehicle's logs. Photos of logs in the trash (or whose vehicle is) are hidden like the logs.
/// </summary>
public sealed class LogPhotoAccess(
    LogAccessGuard guard, IExpenseRepository expenses, IRefuelingRepository refuelings, ILogPhotoRepository photos)
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
        return await guard.ForVehicleAsync(vehicleId, ct);
    }

    /// <summary>The log for changing its photos: unseen means not found, view-only is forbidden.</summary>
    public async Task<LogContext> EditableAsync(LogType logType, Guid logId, CancellationToken ct) =>
        LogAccessGuard.RequireEdit(await FindAsync(logType, logId, ct),
            () => new NotFoundException(logType == LogType.Expense ? "expense.notFound" : "refueling.notFound",
                $"The log {logId} does not exist.", new { Id = logId }));

    /// <summary>Whether the image is a photo of a log the current user may see.</summary>
    public async Task<bool> CanSeeImageAsync(Guid imageId, CancellationToken ct) =>
        await photos.FindByImageAsync(imageId, ct) is { } photo && await FindAsync(photo.LogType, photo.LogId, ct) is not null;
}
