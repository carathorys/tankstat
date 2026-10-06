using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Access;
using Tankstat.Application.Recurring;
using Tankstat.Domain.Recurring;

namespace Tankstat.Infrastructure.Persistence.Repositories;

internal sealed class RecurringExpenseRepository(IDbContextFactory<AppDbContext> dbFactory) : IRecurringExpenseRepository
{
    public async Task<IReadOnlyList<RecurringExpense>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RecurringExpenses.AsNoTracking().Where(r => r.VehicleId == vehicleId).OrderBy(r => r.Title).ThenBy(r => r.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecurringExpense>> ListForVehiclesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RecurringExpenses.AsNoTracking().Where(r => vehicleIds.Contains(r.VehicleId)).OrderBy(r => r.Title).ThenBy(r => r.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListVehicleIdsAsync(OwnerScope scope, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The vehicles' query filter leaves out the trashed ones.
        return await db.RecurringExpenses.InScope(scope, r => r.VehicleId)
            .Where(r => db.Vehicles.Any(v => v.Id == r.VehicleId))
            .Select(r => r.VehicleId).Distinct().ToListAsync(ct);
    }

    public async Task<RecurringExpense?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RecurringExpenses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<RecurringExpense>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RecurringExpenses.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
    }

    public async Task CompleteAsync(IReadOnlyCollection<RecurringExpense> items, IReadOnlyCollection<RecurringCompletion> links, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.RecurringExpenses.UpdateRange(items);
        db.RecurringCompletions.AddRange(links);
        await db.SaveChangesAsync(ct); // one save, one transaction: no link without its moved schedule, and the other way round
    }

    public async Task<IReadOnlyList<CompletedSchedule>> ListCompletionsForExpensesAsync(IReadOnlyCollection<Guid> expenseIds, CancellationToken ct)
    {
        if (expenseIds.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RecurringCompletions.AsNoTracking()
            .Where(c => expenseIds.Contains(c.ExpenseId))
            .Join(db.RecurringExpenses, c => c.RecurringExpenseId, r => r.Id, (c, r) => new { c.ExpenseId, r.Id, r.Title })
            .OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Select(x => new CompletedSchedule(x.ExpenseId, x.Id, x.Title))
            .ToListAsync(ct);
    }

    public async Task AddAsync(RecurringExpense item, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.RecurringExpenses.Add(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(RecurringExpense item, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.RecurringExpenses.Update(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(RecurringExpense item, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.RecurringExpenses.Remove(item);
        await db.SaveChangesAsync(ct);
    }
}
