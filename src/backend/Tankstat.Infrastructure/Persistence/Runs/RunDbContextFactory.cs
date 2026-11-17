using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// The one door to the database (#335). Outside a run it is the pooled factory. Inside one (<see cref="RunTransaction.Current"/>) every
/// context is enlisted in the run's connection and transaction, so whatever the services save commits or rolls back together. Such a
/// context is built unpooled: a pooled one that was enlisted keeps the finished transaction when it is leased again. It does not own
/// the connection, so disposing it leaves the connection open for the next one.
/// </summary>
internal sealed class RunDbContextFactory(IDbContextFactory<AppDbContext> pooled, DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        if (RunTransaction.Current is not { } run) return pooled.CreateDbContext();
        var db = new AppDbContext(options);
        db.Database.SetDbConnection(run.Connection);
        db.Database.UseTransaction(run.Transaction.GetDbTransaction());
        return db;
    }

    /// <summary>A context outside the run's transaction, for what must stay when the run rolls back (the run's marker).</summary>
    public AppDbContext CreateOutside() => pooled.CreateDbContext();
}
