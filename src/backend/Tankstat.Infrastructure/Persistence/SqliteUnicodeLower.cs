using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>
/// SQLite's built-in <c>lower()</c> only folds ASCII, so a search for "é" would miss "É". Every SQLite connection gets a
/// replacement that folds all of Unicode, which is what the other providers do; queries stay as they are (<c>ToLower()</c>).
/// </summary>
internal sealed class SqliteUnicodeLower : DbConnectionInterceptor
{
    public static SqliteUnicodeLower Instance { get; } = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Register(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Register(connection);
        return Task.CompletedTask;
    }

    private static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite) sqlite.CreateFunction<string?, string?>("lower", s => s?.ToLowerInvariant(), isDeterministic: true);
    }
}
