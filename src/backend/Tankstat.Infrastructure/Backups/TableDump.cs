using System.Buffers;
using System.Data.Common;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tankstat.Infrastructure.Backups;

/// <summary>A table of the database as the backup dumps it: its columns, each read the way the EF type mapping reads it, in the order of its key.</summary>
/// <param name="DependsOn">The tables its rows refer to (foreign keys): loaded before it, emptied after it.</param>
internal sealed record DumpTable(string Name, string? Schema, IReadOnlyList<DumpColumn> Columns, IReadOnlyList<string> OrderBy, IReadOnlySet<string> DependsOn);

/// <param name="Mapping">How the column is read and written on this provider; none for the migrations history, which is never loaded.</param>
internal sealed record DumpColumn(string Name, Func<DbDataReader, int, object> Read, RelationalTypeMapping? Mapping);

/// <summary>
/// Every table as JSON Lines: one object per row, the column names as in the database, typed values (strings for GUIDs and ISO 8601 UTC
/// date-times, numbers, booleans, null, base64 for bytes). The tables come from the EF model, so a new table is in the next backup without
/// a code change; the migrations history is dumped for the record. Every value is read the way EF reads it on that provider (the type
/// mapping's reader method), so the same row dumps the same on every database, and the dump never depends on the entity classes.
/// </summary>
internal static class TableDump
{
    public const string MigrationsHistory = "__EFMigrationsHistory";

    /// <summary>The tables of the model (an owned type shares its owner's table), then the migrations history.</summary>
    public static IReadOnlyList<DumpTable> Tables(IModel model)
    {
        var tables = new List<DumpTable>();
        foreach (var group in model.GetEntityTypes().Where(e => e.GetTableName() is not null).GroupBy(e => StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema())))
        {
            var columns = new List<DumpColumn>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in group.SelectMany(e => e.GetProperties()))
            {
                var column = property.GetColumnName(group.Key);
                if (column is null || !seen.Add(column)) continue;
                var mapping = property.GetRelationalTypeMapping();
                columns.Add(new DumpColumn(column, Reader(mapping), mapping));
            }
            var key = group.Select(e => e.FindPrimaryKey()).FirstOrDefault(k => k is not null);
            var orderBy = key?.Properties.Select(p => p.GetColumnName(group.Key)!).ToList() ?? [];
            // The tables this one's rows point at (an owned type points at its owner in the same table, which is no dependency).
            var dependsOn = group.SelectMany(e => e.GetForeignKeys()).Select(fk => fk.PrincipalEntityType.GetTableName())
                .Where(t => t is not null && t != group.Key.Name).Select(t => t!).ToHashSet(StringComparer.Ordinal);
            tables.Add(new DumpTable(group.Key.Name, group.Key.Schema, columns, orderBy, dependsOn));
        }
        tables.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        tables.Add(new DumpTable(MigrationsHistory, null,
            [new DumpColumn("MigrationId", (r, i) => r.GetString(i), null), new DumpColumn("ProductVersion", (r, i) => r.GetString(i), null)], ["MigrationId"],
            new HashSet<string>()));
        return tables;
    }

    /// <summary>Writes the rows of a table as JSON Lines; returns how many rows and how many bytes.</summary>
    public static async Task<(long Rows, long Bytes)> WriteAsync(BackupSource source, ISqlGenerationHelper sql, DumpTable table, Stream output, CancellationToken ct)
    {
        var columns = string.Join(", ", table.Columns.Select(c => sql.DelimitIdentifier(c.Name)));
        var order = table.OrderBy.Count == 0 ? "" : " ORDER BY " + string.Join(", ", table.OrderBy.Select(c => sql.DelimitIdentifier(c)));
        await using var command = source.Command($"SELECT {columns} FROM {sql.DelimitIdentifier(table.Name, table.Schema)}{order}");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var buffer = new ArrayBufferWriter<byte>(4096);
        await using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });
        long rows = 0, bytes = 0;
        while (await reader.ReadAsync(ct))
        {
            writer.WriteStartObject();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                writer.WritePropertyName(table.Columns[i].Name);
                if (reader.IsDBNull(i)) writer.WriteNullValue();
                else WriteValue(writer, table.Columns[i].Read(reader, i));
            }
            writer.WriteEndObject();
            writer.Flush();
            await output.WriteAsync(buffer.WrittenMemory, ct);
            output.WriteByte((byte)'\n');
            bytes += buffer.WrittenCount + 1;
            rows++;
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
        }
        return (rows, bytes);
    }

    internal static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string s: writer.WriteStringValue(s); break;
            case Guid g: writer.WriteStringValue(g.ToString("D")); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case short sh: writer.WriteNumberValue(sh); break;
            case byte by: writer.WriteNumberValue(by); break;
            case decimal m: writer.WriteNumberValue(m); break;
            case double d: writer.WriteNumberValue(d); break;
            case float f: writer.WriteNumberValue(f); break;
            case DateTime dt: writer.WriteStringValue(Utc(dt).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'")); break;
            case DateTimeOffset dto: writer.WriteStringValue(dto.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'")); break;
            case DateOnly date: writer.WriteStringValue(date.ToString("yyyy-MM-dd")); break;
            case TimeOnly time: writer.WriteStringValue(time.ToString("HH:mm:ss.fffffff")); break;
            case TimeSpan span: writer.WriteStringValue(span.ToString("c")); break;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
            default: throw new NotSupportedException($"A backup cannot hold a value of type {value.GetType().Name}.");
        }
    }

    /// <summary>The app stores UTC; a provider that gives the value back without a kind means that, one that gives local time is converted.</summary>
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>Reads a column the way EF materialises it on this provider: <c>GetGuid</c>, <c>GetDateTime</c>, <c>GetFieldValue&lt;T&gt;</c>, ...</summary>
    private static Func<DbDataReader, int, object> Reader(RelationalTypeMapping mapping)
    {
        var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
        var method = mapping.GetDataReaderMethod();
        if (method.IsGenericMethodDefinition) method = method.MakeGenericMethod(providerType);
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        // Some mappings name a method of the provider's own reader class; the reader is cast to it then.
        var target = method.DeclaringType!.IsAssignableFrom(typeof(DbDataReader)) ? (Expression)reader : Expression.Convert(reader, method.DeclaringType);
        var call = Expression.Convert(Expression.Call(target, method, ordinal), typeof(object));
        var read = Expression.Lambda<Func<DbDataReader, int, object>>(call, reader, ordinal).Compile();
        if (providerType != typeof(DateOnly)) return read;
        // Oracle's MySQL connector hands a `date` column back as a DateTime however it is asked (GetFieldValue<DateOnly> throws an
        // InvalidCastException, which also keeps the app itself from reading a log there): such a value is turned into the date it is.
        return (r, i) => r.GetValue(i) is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : read(r, i);
    }
}
