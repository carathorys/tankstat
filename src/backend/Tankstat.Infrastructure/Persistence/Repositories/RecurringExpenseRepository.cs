using Microsoft.EntityFrameworkCore;
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

    public async Task<RecurringExpense?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RecurringExpenses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
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
