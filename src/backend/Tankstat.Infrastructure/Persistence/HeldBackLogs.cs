using Microsoft.EntityFrameworkCore;
using Tankstat.Domain.Photos;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>
/// What holds an import's <i>Replace all</i> back (#338, the owner's decision 2), counted one way for the review (the target reader) and
/// for the purge that checks it again: a backup cannot bring photos or tyre log lines back yet.
/// </summary>
internal static class HeldBackLogs
{
    /// <summary>How many of the vehicle's logs of the kind, live or in the trash, have photos.</summary>
    public static Task<int> CountLogsWithPhotosAsync(this AppDbContext db, LogType logType, Guid vehicleId, CancellationToken ct) =>
        db.LogPhotos.Where(p => p.LogType == logType && p.VehicleId == vehicleId).Select(p => p.LogId).Distinct().CountAsync(ct);

    /// <summary>How many of the vehicle's expenses, live or in the trash, belong to a tyre log line (one in the trash too).</summary>
    public static Task<int> CountExpensesOnTyreLinesAsync(this AppDbContext db, Guid vehicleId, CancellationToken ct) =>
        db.TyreEntries.IgnoreQueryFilters().Where(l => l.VehicleId == vehicleId && l.ExpenseId != null).Select(l => l.ExpenseId).Distinct().CountAsync(ct);
}
