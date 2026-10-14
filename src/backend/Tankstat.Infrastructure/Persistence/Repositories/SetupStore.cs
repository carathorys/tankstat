using Microsoft.EntityFrameworkCore;
using Tankstat.Application.Setup;
using Tankstat.Domain.Instance;
using Tankstat.Domain.Users;

namespace Tankstat.Infrastructure.Persistence.Repositories;

/// <summary>
/// The setup columns of the instance's row, changed only by conditional updates (the row as a whole is <c>InstanceStartService</c>'s): a
/// setup is decided only while undecided and completed only while pending, so two servers starting at once, or two requests of the setup
/// page, never both decide or complete it.
/// </summary>
internal sealed class SetupStore(IDbContextFactory<AppDbContext> dbFactory) : ISetupStore
{
    public async Task<SetupPhase> PhaseAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.InstanceState.AsNoTracking().Where(s => s.Id == InstanceState.SingletonId)
            .Select(s => new { s.SetupPendingSince, s.SetupCompletedAt }).FirstOrDefaultAsync(ct);
        return row is null ? SetupPhase.Undecided
            : row.SetupCompletedAt is not null ? SetupPhase.Completed
            : row.SetupPendingSince is not null ? SetupPhase.Pending
            : SetupPhase.Undecided;
    }

    public async Task<bool> HoldsDataAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(ct) || await db.Vehicles.IgnoreQueryFilters().AnyAsync(ct);
    }

    public async Task<SetupPhase> DecideAsync(bool holdsData, DateTimeOffset now, CancellationToken ct)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await EnsureRowAsync(db, ct);
            var undecided = db.InstanceState.Where(s => s.Id == InstanceState.SingletonId && s.SetupPendingSince == null && s.SetupCompletedAt == null);
            if (holdsData)
                await undecided.ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.SetupCompletedAt, (DateTimeOffset?)now)
                    .SetProperty(s => s.SetupCompletedVia, (SetupCompletion?)SetupCompletion.Upgrade), ct);
            else
                await undecided.ExecuteUpdateAsync(u => u.SetProperty(s => s.SetupPendingSince, (DateTimeOffset?)now), ct);
        }
        return await PhaseAsync(ct);
    }

    public async Task<bool> CompleteAsync(SetupCompletion via, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await CompletePendingAsync(db, via, now, ct);
    }

    public async Task<bool> CompleteWithAdministratorAsync(User administrator, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The row first: a second request waits on its lock here and then finds the setup completed, so it adds nobody.
        if (!await CompletePendingAsync(db, SetupCompletion.Administrator, now, ct)) return false;
        db.Users.Add(administrator);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static async Task<bool> CompletePendingAsync(AppDbContext db, SetupCompletion via, DateTimeOffset now, CancellationToken ct) =>
        await db.InstanceState
            .Where(s => s.Id == InstanceState.SingletonId && s.SetupPendingSince != null && s.SetupCompletedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.SetupCompletedAt, (DateTimeOffset?)now)
                .SetProperty(s => s.SetupCompletedVia, (SetupCompletion?)via), ct) > 0;

    /// <summary>The row exists after every start's restore step; a database no start has seen (a tool's) gets one, the other start's kept when two race.</summary>
    private static async Task EnsureRowAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.InstanceState.AnyAsync(s => s.Id == InstanceState.SingletonId, ct)) return;
        db.InstanceState.Add(InstanceState.New());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
    }
}
