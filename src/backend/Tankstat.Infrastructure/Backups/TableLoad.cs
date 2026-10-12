using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Tankstat.Application.Backups;
using Tankstat.Domain;
using Tankstat.Domain.Instance;
using Tankstat.Infrastructure.Persistence;

namespace Tankstat.Infrastructure.Backups;

/// <summary>
/// Loads the tables of a backup (<c>tables/&lt;Table&gt;.jsonl</c>, as <see cref="TableDump"/> wrote them) into the database, replacing
/// every row, in one transaction: the tables are emptied in reverse foreign-key order and filled in forward order (the model's foreign keys
/// give the order; a cycle is refused), every value typed by the column's EF mapping, so a dump of one provider loads into another. The
/// migrations history is never written (the backup's migration must already be the database's). Last of all, inside the same transaction,
/// the instance gets a new generation, so the backup's own never survives. A row count that differs from the manifest fails the load.
/// </summary>
internal static class TableLoad
{
    /// <summary>At most this many rows per INSERT; fewer when a table has many columns (SQL Server allows 2100 parameters, SQLite 999 on older builds).</summary>
    private const int ParametersPerStatement = 900;

    /// <summary>The model's tables in load order: a table after every table it refers to; the migrations history left out.</summary>
    public static IReadOnlyList<DumpTable> InLoadOrder(IReadOnlyList<DumpTable> tables)
    {
        var pending = tables.Where(t => t.Name != TableDump.MigrationsHistory).ToDictionary(t => t.Name, StringComparer.Ordinal);
        var ordered = new List<DumpTable>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var ready = pending.Values.Where(t => t.DependsOn.All(d => placed.Contains(d) || !pending.ContainsKey(d))).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            if (ready.Count == 0) throw new InvalidOperationException("The tables refer to each other in a cycle: " + string.Join(", ", pending.Keys.Order()));
            foreach (var table in ready)
            {
                ordered.Add(table);
                placed.Add(table.Name);
                pending.Remove(table.Name);
            }
        }
        return ordered;
    }

    /// <summary>Replaces every row of the database with the backup's and stamps a new generation; returns what the instance is now.</summary>
    public static async Task<InstanceInfo> LoadAsync(
        AppDbContext db, string tablesFolder, IReadOnlyDictionary<string, long> expected, DateTimeOffset now, DateTimeOffset restorePoint, ILogger logger, CancellationToken ct)
    {
        var tables = InLoadOrder(TableDump.Tables(db.Model));
        var sql = db.GetService<ISqlGenerationHelper>();
        foreach (var unknown in expected.Keys.Where(k => k != TableDump.MigrationsHistory && tables.All(t => t.Name != k)))
            logger.LogWarning("The backup holds a table this release does not know, which is not loaded: {Table}", unknown);

        await db.Database.OpenConnectionAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        foreach (var table in tables.AsEnumerable().Reverse())
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = dbTransaction;
            delete.CommandText = $"DELETE FROM {sql.DelimitIdentifier(table.Name, table.Schema)}";
            await delete.ExecuteNonQueryAsync(ct);
        }
        var loaded = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            var file = Path.Combine(tablesFolder, table.Name + ".jsonl");
            // A table the backup has no file for is empty, unless the manifest says it had rows: then the file went missing, and loading
            // on would silently empty the table.
            loaded[table.Name] = File.Exists(file) ? await InsertAsync(connection, dbTransaction, sql, table, file, ct) : 0;
            if (expected.TryGetValue(table.Name, out var count) && count != loaded[table.Name])
                throw new DomainException("backup.corrupt", $"The backup's manifest says {count} rows of {table.Name}, the file holds {loaded[table.Name]}.", new { Table = table.Name, Expected = count, Actual = loaded[table.Name] });
        }

        // The generation is this instance's own, never the backup's: stamped after the rows, inside the transaction.
        // The backup's own row comes along (its last start mode too, so the start that follows signs out sessions it brought from another mode).
        var instance = await db.InstanceState.SingleOrDefaultAsync(ct);
        if (instance is null)
        {
            instance = InstanceState.New();
            db.InstanceState.Add(instance);
        }
        instance.Restored(now, restorePoint);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Loaded {Tables} tables with {Rows} rows from the backup", loaded.Count, loaded.Values.Sum());
        return new InstanceInfo(instance.Generation, instance.RestorePoint);
    }

    private static async Task<long> InsertAsync(DbConnection connection, DbTransaction transaction, ISqlGenerationHelper sql, DumpTable table, string file, CancellationToken ct)
    {
        var columns = table.Columns;
        var rowsPerStatement = Math.Max(1, Math.Min(100, ParametersPerStatement / Math.Max(1, columns.Count)));
        var head = $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} ({string.Join(", ", columns.Select(c => sql.DelimitIdentifier(c.Name)))}) VALUES ";
        long total = 0;
        var batch = new List<JsonDocument>(rowsPerStatement);
        await foreach (var line in File.ReadLinesAsync(file, ct))
        {
            if (line.Length == 0) continue;
            batch.Add(JsonDocument.Parse(line));
            if (batch.Count == rowsPerStatement)
            {
                total += await InsertBatchAsync(connection, transaction, sql, head, columns, batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0) total += await InsertBatchAsync(connection, transaction, sql, head, columns, batch, ct);
        return total;
    }

    private static async Task<int> InsertBatchAsync(
        DbConnection connection, DbTransaction transaction, ISqlGenerationHelper sql, string head, IReadOnlyList<DumpColumn> columns, List<JsonDocument> rows, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var values = new List<string>(rows.Count);
        var n = 0;
        foreach (var row in rows)
        {
            var names = new List<string>(columns.Count);
            foreach (var column in columns)
            {
                var name = sql.GenerateParameterName($"p{n++}");
                names.Add(name);
                var mapping = column.Mapping ?? throw new InvalidOperationException($"{column.Name} has no type mapping.");
                var value = row.RootElement.TryGetProperty(column.Name, out var element) && element.ValueKind != JsonValueKind.Null
                    ? FromJson(element, mapping)
                    : null;
                command.Parameters.Add(mapping.CreateParameter(command, name, value, nullable: value is null ? true : null));
            }
            values.Add("(" + string.Join(", ", names) + ")");
            row.Dispose();
        }
        command.CommandText = head + string.Join(", ", values);
        await command.ExecuteNonQueryAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// The value as the mapping writes it: the dump holds what the provider gave (<see cref="TableDump"/> reads with the mapping's reader
    /// method), so it is parsed into the provider's type and taken back through the converter, which <c>CreateParameter</c> applies again.
    /// </summary>
    private static object FromJson(JsonElement element, RelationalTypeMapping mapping)
    {
        var providerType = Nullable.GetUnderlyingType(mapping.Converter?.ProviderClrType ?? mapping.ClrType) ?? mapping.Converter?.ProviderClrType ?? mapping.ClrType;
        var value = Parse(element, providerType);
        return mapping.Converter is { } converter ? converter.ConvertFromProvider(value) ?? value : value;
    }

    internal static object Parse(JsonElement e, Type type)
    {
        try
        {
            if (type == typeof(string)) return e.GetString()!;
            if (type == typeof(Guid)) return Guid.Parse(e.GetString()!);
            if (type == typeof(bool)) return e.GetBoolean();
            if (type == typeof(int)) return e.GetInt32();
            if (type == typeof(long)) return e.GetInt64();
            if (type == typeof(short)) return e.GetInt16();
            if (type == typeof(byte)) return e.GetByte();
            if (type == typeof(decimal)) return e.GetDecimal();
            if (type == typeof(double)) return e.GetDouble();
            if (type == typeof(float)) return e.GetSingle();
            if (type == typeof(DateTime)) return DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(e.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            if (type == typeof(DateOnly)) return DateOnly.ParseExact(e.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (type == typeof(TimeOnly)) return TimeOnly.Parse(e.GetString()!, CultureInfo.InvariantCulture);
            if (type == typeof(TimeSpan)) return TimeSpan.Parse(e.GetString()!, CultureInfo.InvariantCulture);
            if (type == typeof(byte[])) return e.GetBytesFromBase64();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            throw new DomainException("backup.corrupt", $"A value in the backup is not a {type.Name}.");
        }
        throw new NotSupportedException($"A backup cannot hold a value of type {type.Name}.");
    }
}
