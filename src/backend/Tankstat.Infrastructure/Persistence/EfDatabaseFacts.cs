using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Backups;

namespace Tankstat.Infrastructure.Persistence;

/// <summary>How large the database is (each provider asked its own way) and the migration it is at.</summary>
internal sealed class EfDatabaseFacts(IDbContextFactory<AppDbContext> dbFactory, IOptions<DatabaseOptions> options, ILogger<EfDatabaseFacts> logger) : IDatabaseFacts
{
    public async Task<long?> SizeAsync(CancellationToken ct)
    {
        var sql = options.Value.Provider switch
        {
            DatabaseProvider.Sqlite => "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()",
            DatabaseProvider.PostgreSql => "SELECT pg_database_size(current_database())",
            DatabaseProvider.SqlServer => "SELECT CAST(SUM(CAST(size AS bigint)) * 8192 AS bigint) FROM sys.database_files",
            DatabaseProvider.MySql => "SELECT COALESCE(SUM(data_length + index_length), 0) FROM information_schema.tables WHERE table_schema = DATABASE()",
            _ => null,
        };
        if (sql is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? null : Convert.ToInt64(value);
        }
        catch (DbException e)
        {
            // The size is information only (a login without the right to look, say): the page shows it as unknown.
            logger.LogDebug(e, "The size of the database could not be read");
            return null;
        }
    }

    public async Task<string?> LastMigrationAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
    }
}
