using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// Several saves of one context that must hold together. Outside a run it is a transaction of the context's own. Inside a run (#335) the
/// context is already in the run's transaction, where a second one is refused: there it is a savepoint, released when committed and
/// rolled back to when not. Either way: commit, or dispose without it to undo.
/// </summary>
internal sealed class InnerTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction _transaction;
    private readonly string? _savepoint;
    private bool _done;

    private InnerTransaction(IDbContextTransaction transaction, string? savepoint)
    {
        _transaction = transaction;
        _savepoint = savepoint;
    }

    public static async Task<InnerTransaction> BeginAsync(AppDbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not { } outer) return new InnerTransaction(await db.Database.BeginTransactionAsync(ct), null);
        var savepoint = RunTransaction.NextSavepoint();
        await outer.CreateSavepointAsync(savepoint, ct);
        return new InnerTransaction(outer, savepoint);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_savepoint is null) await _transaction.CommitAsync(ct);
        else await _transaction.ReleaseSavepointAsync(_savepoint, ct);
        _done = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_savepoint is null) await _transaction.DisposeAsync(); // rolls back what was not committed
        else if (!_done) await _transaction.RollbackToSavepointAsync(_savepoint);
    }
}

internal static class InnerTransactionExtensions
{
    /// <summary>See <see cref="InnerTransaction"/>.</summary>
    public static Task<InnerTransaction> BeginInnerAsync(this AppDbContext db, CancellationToken ct) => InnerTransaction.BeginAsync(db, ct);
}
