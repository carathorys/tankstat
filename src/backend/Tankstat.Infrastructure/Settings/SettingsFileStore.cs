using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Infrastructure.Settings;

/// <summary>
/// The app's own settings file as JSON on disk. It is read through the same JSON configuration provider the app starts with, so the keys are
/// exactly the configuration's (<c>Auth:Mode</c>, <c>Auth:AdminEmails:0</c>). It is written as nested JSON (a run of <c>0</c>, <c>1</c>, ...
/// children is an array), every value a string as the configuration sees it, one write at a time: the new content goes to a temporary file
/// next to it that only the owner may read, the previous file is copied to <c>.bak</c>, then the temporary file takes its place.
/// </summary>
public sealed class SettingsFileStore(string path) : ISettingsFileStore
{
    /// <summary>Owner may read and write; nobody else: the file holds secrets (an SMTP password, an OIDC client secret, an API key).</summary>
    public const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly SemaphoreSlim _writing = new(1, 1);

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public string BackupPath => Path + ".bak";

    public Task<IReadOnlyDictionary<string, string?>> ReadAsync(CancellationToken ct) => Task.FromResult(Read(Path));

    /// <summary>The leaf values of the JSON file at <paramref name="path"/> by configuration key; empty when there is no file.</summary>
    public static IReadOnlyDictionary<string, string?> Read(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full)) return new Dictionary<string, string?>();
        var root = new ConfigurationBuilder().Add(SourceFor(full, optional: false)).Build();
        return Leaves(root).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The file at <paramref name="path"/> as a JSON configuration source that is read once (nothing reloads), the way the app reads its own
    /// settings file at start and the Seeder reads it too. The file's folder is the provider's root and nothing in it is hidden: the provider
    /// <c>AddJsonFile</c> makes would not see a file whose name starts with a dot, and read it as missing.
    /// </summary>
    public static JsonConfigurationSource SourceFor(string path, bool optional = true)
    {
        var full = System.IO.Path.GetFullPath(path);
        var folder = System.IO.Path.GetDirectoryName(full);
        var source = new JsonConfigurationSource { Path = full, Optional = optional, ReloadOnChange = false };
        if (folder is not null && Directory.Exists(folder))
        {
            source.FileProvider = new PhysicalFileProvider(folder, ExclusionFilters.None);
            source.Path = System.IO.Path.GetFileName(full);
        }
        else source.ResolveFileProvider(); // no folder, so no file there: an optional source reads as empty
        return source;
    }

    private static IEnumerable<KeyValuePair<string, string?>> Leaves(IConfiguration section)
    {
        foreach (var child in section.GetChildren())
        {
            if (child.GetChildren().Any()) foreach (var leaf in Leaves(child)) yield return leaf;
            else yield return new(child.Path, child.Value);
        }
    }

    public async Task WriteAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var text = ToJson(values);
        await _writing.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temporary = Path + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerOnly;
            await using (var stream = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(text.AsMemory(), ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, OwnerOnly); // a file that was already there kept its own mode
            if (File.Exists(Path))
            {
                File.Copy(Path, BackupPath, overwrite: true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(BackupPath, OwnerOnly);
            }
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>The flat <paramref name="values"/> as nested, indented JSON; a null value is left out, as is a key with nothing after a separator.</summary>
    public static string ToJson(IReadOnlyDictionary<string, string?> values)
    {
        var root = new JsonObject();
        foreach (var (key, value) in values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (value is null) continue;
            var parts = key.Split(':');
            if (parts.Any(string.IsNullOrEmpty)) continue;
            var node = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (node[parts[i]] is not JsonObject next)
                {
                    next = new JsonObject();
                    node[parts[i]] = next;
                }
                node = next;
            }
            node[parts[^1]] = value;
        }
        return ArraysOf(root).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + Environment.NewLine;
    }

    /// <summary>An object whose keys are exactly 0..n-1 is a list: written as an array, read back as the same keys.</summary>
    private static JsonNode ArraysOf(JsonObject node)
    {
        var keys = node.Select(p => p.Key).ToList();
        var indexed = keys.Count > 0 && keys.All(k => int.TryParse(k, out var n) && n >= 0 && n < keys.Count) && keys.Select(int.Parse).Distinct().Count() == keys.Count;
        if (indexed)
        {
            var array = new JsonArray();
            foreach (var key in keys.OrderBy(int.Parse)) array.Add(Converted(node[key]!));
            return array;
        }
        var result = new JsonObject();
        foreach (var key in keys) result[key] = Converted(node[key]!);
        return result;
    }

    private static JsonNode Converted(JsonNode child) => child is JsonObject o ? ArraysOf(o) : JsonValue.Create(child.GetValue<string>())!;
}
