using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>
/// Where the app's own settings file sits among the configuration sources: below it the shipped <c>appsettings</c> files, above it the
/// environment variables and the command line, which therefore keep winning. Kept so a candidate configuration (the same sources with other
/// file values) can be built and checked before the file is written.
/// </summary>
public sealed class SettingsSources(IReadOnlyList<IConfigurationSource> below, IReadOnlyList<IConfigurationSource> above, IDictionary<string, object> properties)
{
    public IReadOnlyList<IConfigurationSource> Below { get; } = below;
    public IReadOnlyList<IConfigurationSource> Above { get; } = above;

    /// <summary>The whole configuration as it would be with <paramref name="file"/> as the settings file's content.</summary>
    public IConfigurationRoot With(IConfigurationSource file)
    {
        var builder = new ConfigurationBuilder();
        foreach (var (key, value) in properties) builder.Properties[key] = value; // the content root's file provider, which the appsettings sources resolve against
        foreach (var source in Below) builder.Add(source);
        builder.Add(file);
        foreach (var source in Above) builder.Add(source);
        return builder.Build();
    }

    /// <summary>The whole configuration with <paramref name="values"/> in the settings file's place.</summary>
    public IConfigurationRoot With(IReadOnlyDictionary<string, string?> values) => With(new MemoryConfigurationSource { InitialData = values });
}

/// <summary>The settings file as this process started: its status and its place among the sources.</summary>
public sealed record SettingsFile(SettingsFileStatus Status, SettingsSources Sources)
{
    public const string PathSetting = "Settings:Path";
    public const string DefaultPath = "settings.json";

    /// <summary>
    /// Puts the app's own settings file (<c>Settings:Path</c>, default <c>settings.json</c> in the working directory) among the sources of
    /// <paramref name="builder"/>, right below the environment variables, unless the app could not start with it: then it is left out and the
    /// status says so (an Error line and a public notice follow once the host runs). A file the environment cannot be started without either
    /// is left out too, and the start fails on the environment as it always did. The path comes from the sources already there (the
    /// environment, the command line, the appsettings files), never from the file itself.
    /// </summary>
    public static SettingsFile Prepare(WebApplicationBuilder builder)
    {
        var path = System.IO.Path.GetFullPath(builder.Configuration[PathSetting] is { } configured && !string.IsNullOrWhiteSpace(configured) ? configured : DefaultPath);
        var configuration = (IConfigurationBuilder)builder.Configuration;
        var sources = configuration.Sources;
        var at = IndexOfEnvironment(sources);
        var placement = new SettingsSources([.. sources.Take(at)], [.. sources.Skip(at)], configuration.Properties);
        var file = new JsonConfigurationSource { Path = path, Optional = true, ReloadOnChange = false }; // nothing reloads: every change waits for a restart
        file.ResolveFileProvider();

        if (!File.Exists(path)) return new(SettingsFileStatus.Used(path, exists: false), placement);
        var problems = Problems(placement, file);
        if (problems.Count == 0 || SettingsCheck.Check(builder.Configuration).Count > 0)
        {
            // The file is fine, or the environment is wrong on its own (the start fails as it always did, on the environment's settings).
            sources.Insert(at, file);
            return new(SettingsFileStatus.Used(path, exists: true), placement);
        }
        return new(SettingsFileStatus.LeftOut(path, problems), placement);
    }

    private static IReadOnlyList<string> Problems(SettingsSources placement, JsonConfigurationSource file)
    {
        try
        {
            return SettingsCheck.Check(placement.With(file));
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException)
        {
            return ["The file is not valid JSON."]; // the parser's text would quote the file
        }
    }

    /// <summary>Right before the last environment variables source without a prefix (the one every <c>Section__Key</c> variable comes through).</summary>
    private static int IndexOfEnvironment(IList<IConfigurationSource> sources)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" }) return i;
        return sources.Count;
    }
}
