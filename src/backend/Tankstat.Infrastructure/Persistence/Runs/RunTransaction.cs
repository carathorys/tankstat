using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tankstat.Infrastructure.Persistence.Runs;

/// <summary>
/// The connection and transaction a run's contexts share (#335), carried by the execution context like <c>RunContext</c>: while it is
/// set, <see cref="RunDbContextFactory"/> hands out contexts enlisted in it.
/// </summary>
internal sealed class RunTransaction(DbConnection connection, IDbContextTransaction transaction)
{
    private static readonly AsyncLocal<RunTransaction?> Ambient = new();
    private static long _savepoints;

    public DbConnection Connection { get; } = connection;

    /// <summary>The root context's transaction: savepoints go through it, so each provider's own rules apply (SQL Server releases none).</summary>
    public IDbContextTransaction Transaction { get; } = transaction;

    public static RunTransaction? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    /// <summary>A savepoint name no other one of this process has: short (SQL Server allows 32 characters) and never reused while nested.</summary>
    public static string NextSavepoint() => $"tk{Interlocked.Increment(ref _savepoints)}";
}
