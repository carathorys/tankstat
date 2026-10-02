using Tankstat.Domain.Recurring;

namespace Tankstat.Application.Recurring;

public interface IRecurringExpenseRepository
{
    /// <summary>All recurring expenses of a vehicle, by title.</summary>
    Task<IReadOnlyList<RecurringExpense>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>The recurring expenses of all the given vehicles in one query, by title.</summary>
    Task<IReadOnlyList<RecurringExpense>> ListForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct);

    Task<RecurringExpense?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(RecurringExpense item, CancellationToken ct);
    Task UpdateAsync(RecurringExpense item, CancellationToken ct);
    Task RemoveAsync(RecurringExpense item, CancellationToken ct);
}
