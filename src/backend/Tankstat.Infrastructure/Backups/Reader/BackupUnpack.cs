using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tankstat.Application.Backups;
using Tankstat.Domain;

namespace Tankstat.Infrastructure.Backups.Reader;

/// <summary>How much the reader lets a file unpack to, whatever the file says about itself.</summary>
/// <param name="ArchiveBytes">The inner archive in plain.</param>
/// <param name="TablesBytes">The loaded tables' rows together, as text.</param>
/// <param name="RowBytes">One row's line.</param>
internal sealed record UnpackLimits(long ArchiveBytes, long TablesBytes, int RowBytes = 1024 * 1024);

/// <summary>
/// Unpacking for the reader (#336), which reads files anyone may have made. Unlike <see cref="BackupFile.Extract"/> it never writes a
/// name that comes from the archive and never unpacks the whole of it: the inner archive is copied out in plain (kept for its session,
/// so single files can be taken later without the key), then only the allowlisted tables' rows are written, each under the model's own
/// name, counted as they are written and stopped at the limits. The server's keys and the settings entry are never unpacked.
/// </summary>
internal static class BackupUnpack
{
    /// <summary>
    /// The inner archive in plain at <paramref name="target"/>: copied, or decrypted with the identity (checked against the manifest's
    /// recipient first, <c>backup.wrongKey</c>). More bytes than the limit, however the entry was stored, is no backup.
    /// </summary>
    public static async Task InnerArchiveAsync(string path, BackupManifest manifest, byte[]? identity, string target, long maxBytes, CancellationToken ct)
    {
        if (manifest.Encryption is { } encryption)
        {
            if (identity is null) throw new DomainException("backup.wrongKey", "The backup is encrypted; the identity or the passphrase is needed.");
            if (!BackupKeys.Opens(identity, encryption.Recipient)) throw new DomainException("backup.wrongKey", "The identity does not open this backup.");
        }
        try
        {
            using var zip = ZipFile.OpenRead(path);
            await using var data = (zip.GetEntry(manifest.Archive.Name) ?? throw NotABackup()).Open();
            await using var plain = manifest.Encryption is null ? data : AgeStream.Decrypt(data, identity!, leaveOpen: true);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await CopyAtMostAsync(plain, output, maxBytes, ct);
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            throw NotABackup();
        }
    }

    /// <summary>
    /// Writes the rows of the tables the reader loads (<see cref="ScratchTables"/>) from the inner archive into <paramref name="tablesFolder"/>,
    /// the tables as <paramref name="model"/> (the backup's migration) has them, <c>Users</c> reduced to who is who. Returns the row counts
    /// the load is to find: the manifest's for a loaded table, none for every other, whose file is not written.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, long>> TablesAsync(
        string innerArchive, IModel model, BackupManifest manifest, string tablesFolder, UnpackLimits limits, bool keepEmails, CancellationToken ct)
    {
        Directory.CreateDirectory(tablesFolder);
        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        long written = 0;
        try
        {
            using var zip = ZipFile.OpenRead(innerArchive);
            foreach (var table in TableDump.Tables(model))
            {
                if (table.Name == TableDump.MigrationsHistory) continue;
                if (!ScratchTables.IsLoaded(table.Name))
                {
                    expected[table.Name] = 0;
                    continue;
                }
                expected[table.Name] = manifest.Contents.Tables.GetValueOrDefault(table.Name);
                if (zip.GetEntry($"tables/{table.Name}.jsonl") is not { } entry) continue; // an empty table; the load checks the count
                var reduce = table.Name == ScratchTables.Users ? UserReduction.For(model, keepEmails) : null;
                await using var source = entry.Open();
                await using var output = new FileStream(Path.Combine(tablesFolder, table.Name + ".jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                written = await CopyRowsAsync(source, output, reduce, written, limits, ct);
            }
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or DecoderFallbackException)
        {
            throw NotABackup();
        }
        return expected;
    }

    /// <summary>Copies a table's rows line by line, a row never longer than its limit and all rows together never past theirs; returns the running total.</summary>
    private static async Task<long> CopyRowsAsync(Stream source, Stream output, UserReduction? reduce, long written, UnpackLimits limits, CancellationToken ct)
    {
        var buffer = new byte[81920];
        var line = new MemoryStream();
        async Task FlushLineAsync()
        {
            if (line.Length == 0) return;
            ReadOnlyMemory<byte> row = line.GetBuffer().AsMemory(0, (int)line.Length);
            if (reduce is not null) row = reduce.Reduce(row);
            written += row.Length + 1;
            if (written > limits.TablesBytes) throw NotABackup();
            await output.WriteAsync(row, ct);
            output.WriteByte((byte)'\n');
            line.SetLength(0);
        }
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            var start = 0;
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\n') continue;
                line.Write(buffer, start, i - start);
                if (line.Length > limits.RowBytes) throw NotABackup();
                await FlushLineAsync();
                start = i + 1;
            }
            line.Write(buffer, start, read - start);
            if (line.Length > limits.RowBytes) throw NotABackup();
        }
        await FlushLineAsync();
        return written;
    }

    private static async Task CopyAtMostAsync(Stream source, Stream target, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > limit) throw NotABackup();
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static DomainException NotABackup() => new("backup.corrupt", "The file is damaged or is not a Tankstat backup.");

    /// <summary>
    /// A user's row with only who they are: the kept columns as they were, every other one empty, or where the table allows no empty value,
    /// a stand-in of its type (one made from the row's id where the column must be unique). Worked out from the backup migration's own
    /// model, so a column a later release added is blanked too.
    /// </summary>
    private sealed class UserReduction
    {
        private readonly List<(string Column, bool Keep, bool Nullable, bool Unique, Type Type)> _columns = [];

        public static UserReduction For(IModel model, bool keepEmails)
        {
            var reduction = new UserReduction();
            var table = TableDump.Tables(model).Single(t => t.Name == ScratchTables.Users);
            var entities = model.GetEntityTypes().Where(e => e.GetTableName() == ScratchTables.Users).ToList();
            var store = StoreObjectIdentifier.Table(ScratchTables.Users, null);
            foreach (var column in table.Columns)
            {
                var properties = entities.SelectMany(e => e.GetProperties()).Where(p => p.GetColumnName(store) == column.Name).ToList();
                var mapping = column.Mapping!;
                var type = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
                reduction._columns.Add((
                    column.Name,
                    ScratchTables.UserColumns.Contains(column.Name) || keepEmails && column.Name == ScratchTables.UserEmail,
                    properties.All(p => p.IsColumnNullable(store)),
                    // Unique for the database only where every column of the index must hold a value (an empty one never clashes).
                    properties.Any(p => p.IsKey() || p.GetContainingIndexes().Any(i => i.IsUnique && i.Properties.All(x => !x.IsColumnNullable(store)))),
                    Nullable.GetUnderlyingType(type) ?? type));
            }
            return reduction;
        }

        public ReadOnlyMemory<byte> Reduce(ReadOnlyMemory<byte> line)
        {
            using var row = JsonDocument.Parse(line);
            var id = row.RootElement.TryGetProperty("Id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw NotABackup();
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                foreach (var (column, keep, nullable, unique, type) in _columns)
                {
                    json.WritePropertyName(column);
                    if (keep && row.RootElement.TryGetProperty(column, out var kept)) kept.WriteTo(json);
                    else if (nullable) json.WriteNullValue();
                    else if (type == typeof(string)) json.WriteStringValue(unique ? id : "");
                    else if (type == typeof(Guid)) json.WriteStringValue(unique ? id : Guid.Empty.ToString());
                    else if (type == typeof(bool)) json.WriteBooleanValue(false);
                    else if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) json.WriteStringValue("2000-01-01T00:00:00.0000000Z");
                    else if (type == typeof(DateOnly)) json.WriteStringValue("2000-01-01");
                    else if (type == typeof(byte[])) json.WriteStringValue("");
                    else json.WriteNumberValue(0);
                }
                json.WriteEndObject();
            }
            return buffer.ToArray();
        }
    }
}
