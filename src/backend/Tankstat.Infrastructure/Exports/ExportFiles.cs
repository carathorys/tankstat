using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tankstat.Application.Exports;

namespace Tankstat.Infrastructure.Exports;

/// <summary>
/// The exports' folder (#341, <c>Exports:Folder</c>, read once like every folder): each export as <c>&lt;id&gt;.zip</c> with its record
/// <c>&lt;id&gt;.json</c>, both owner-only on Unix (a file is someone's cars, in plain unless they chose a passphrase). No table, so no
/// migration, and a restore of the database leaves them.
/// </summary>
internal sealed class ExportFiles(IOptionsMonitor<ExportOptions> options, ILogger<ExportFiles> logger) : IExportFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly Lock Gate = new();

    public string Root { get; } = options.CurrentValue.Root;

    public IReadOnlyList<ExportRecord> Records()
    {
        if (!Directory.Exists(Root)) return [];
        var records = new List<ExportRecord>();
        lock (Gate)
            foreach (var path in Directory.EnumerateFiles(Root, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ExportRecord>(File.ReadAllText(path), Json) is { } record && Path.GetFileNameWithoutExtension(path) == record.Id.ToString("N"))
                        records.Add(record);
                }
                catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("An export's record cannot be read ({Failure}); it is left alone", e.GetType().Name);
                }
            }
        return records;
    }

    public void Save(ExportRecord record)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, $"{record.Id:N}.json");
        var temporary = path + ".tmp";
        lock (Gate)
        {
            using (var stream = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = OwnerOnly() }))
                JsonSerializer.Serialize(stream, record, Json);
            File.Move(temporary, path, overwrite: true);
        }
    }

    public string FileOf(Guid id) => Path.Combine(Root, $"{id:N}.zip");

    public Stream? OpenRead(Guid id)
    {
        try
        {
            return new FileStream(FileOf(id), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void DeleteFile(Guid id) => File.Delete(FileOf(id));

    public void DeleteRecord(Guid id)
    {
        lock (Gate) File.Delete(Path.Combine(Root, $"{id:N}.json"));
    }

    public void RemovePartials()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var partial in Directory.EnumerateFiles(Root, "*.partial"))
        {
            try
            {
                // One a run is writing is held open without sharing: only a stopped run's goes.
                using (new FileStream(partial, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            }
            catch (IOException)
            {
                // Being written.
            }
        }
    }

    private static UnixFileMode? OwnerOnly() => OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite;
}
