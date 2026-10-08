using Tankstat.Application.Access;
using Tankstat.Domain.Recurring;

namespace Tankstat.Application.Recurring;

public interface IRecurringExpenseRepository
{
    /// <summary>All recurring expenses of a vehicle, by title.</summary>
    Task<IReadOnlyList<RecurringExpense>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct);

    /// <summary>The recurring expenses of all the given vehicles in one query, by title.</summary>
    Task<IReadOnlyList<RecurringExpense>> ListForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct);

    /// <summary>The vehicles (not trashed ones) in the scope that have at least one recurring expense.</summary>
    Task<IReadOnlyList<Guid>> ListVehicleIdsAsync(OwnerScope scope, CancellationToken ct);

    Task<RecurringExpense?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>The given schedules (of any vehicle) in no particular order; ids that do not exist are simply absent.</summary>
    Task<IReadOnlyList<RecurringExpense>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Saves the moved baselines and the links to the expense that covered them in one save: all of it is stored, or none.</summary>
    Task CompleteAsync(IReadOnlyCollection<RecurringExpense> items, IReadOnlyCollection<RecurringCompletion> links, CancellationToken ct);

    /// <summary>Which schedules the given expenses covered, with their titles as they are now; schedules deleted since are left out.</summary>
    Task<IReadOnlyList<CompletedSchedule>> ListCompletionsForExpensesAsync(IReadOnlyCollection<Guid> expenseIds, CancellationToken ct);

    /// <summary>Saves a new schedule; false when one with its id already existed (a concurrent add with the same client id won).</summary>
    Task<bool> AddAsync(RecurringExpense item, CancellationToken ct);
    Task UpdateAsync(RecurringExpense item, CancellationToken ct);
    Task RemoveAsync(RecurringExpense item, CancellationToken ct);
}

/// <summary>A schedule an expense covered when it was marked done.</summary>
public sealed record CompletedSchedule(Guid ExpenseId, Guid RecurringExpenseId, string Title);
