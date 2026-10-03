using Tankstat.Application.Access;
using Tankstat.Application.Photos;
using Tankstat.Domain.Vehicles;

namespace Tankstat.Application.Expenses;

public interface IExpenseRepository
{
    /// <summary>One page of a vehicle's expenses that are not in the trash.</summary>
    Task<IReadOnlyList<Expense>> ListForVehicleAsync(Guid vehicleId, ExpenseQuery query, CancellationToken ct);
    Task<int> CountForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>Whether the vehicle has any expense, trashed ones included.</summary>
    Task<bool> AnyForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>All live expenses of a vehicle (date, title, amount), to recognise duplicates when importing.</summary>
    Task<IReadOnlyList<Expense>> ListAllForVehicleAsync(Guid vehicleId, CancellationToken ct);

    Task<IReadOnlyList<Expense>> ListDeletedAsync(OwnerScope scope, ExpenseQuery query, CancellationToken ct);
    Task<int> CountDeletedAsync(OwnerScope scope, CancellationToken ct);

    /// <summary>The distinct categories used on a vehicle's expenses, for suggestions.</summary>
    Task<IReadOnlyList<string>> CategoriesAsync(Guid vehicleId, CancellationToken ct);

    Task<Expense?> FindAsync(Guid id, CancellationToken ct);
    Task<Expense?> FindIncludingDeletedAsync(Guid id, CancellationToken ct);

    Task AddAsync(Expense expense, CancellationToken ct);

    /// <param name="changes">Readings and costs this update created (they are inserted) or let go of (they are deleted).</param>
    Task UpdateAsync(Expense expense, LinkedChanges changes, CancellationToken ct);

    /// <summary>Physically removes the trashed expenses in scope, with their photo rows. Returns which ones, so their photo files can be removed.</summary>
    Task<PurgedLogs> PurgeAsync(OwnerScope scope, CancellationToken ct);
}
