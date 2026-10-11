using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Tankstat.Application.SystemSettings;

namespace Tankstat.Api.Settings;

/// <summary>Helpers over the configuration sources: where the settings file sits among them, and a configuration with other values in its place.</summary>
public static class ConfigurationSources
{
    /// <summary>Right before the last environment variables source without a prefix (the one every <c>Section__Key</c> variable comes through).</summary>
    public static int IndexOfEnvironment(IList<IConfigurationSource> sources)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" }) return i;
        return sources.Count;
    }

    /// <summary>
    /// A configuration built from <paramref name="original"/>'s sources with <paramref name="replacement"/> at <paramref name="at"/>, in place of the
    /// source there (<paramref name="replacing"/>) or inserted before it. The builder's properties come along: the content root's file provider,
    /// which the <c>appsettings</c> sources resolve against.
    /// </summary>
    public static IConfigurationRoot With(IConfigurationBuilder original, IConfigurationSource replacement, int at, bool replacing)
    {
        var builder = new ConfigurationBuilder();
        foreach (var (key, value) in original.Properties) builder.Properties[key] = value;
        for (var i = 0; i < original.Sources.Count; i++)
        {
            if (i == at) builder.Add(replacement);
            if (i != at || !replacing) builder.Add(original.Sources[i]);
        }
        if (at >= original.Sources.Count) builder.Add(replacement);
        return builder.Build();
    }
}

/// <summary>The settings file as this process started: its status and the source that is (or would have been) its place among the sources.</summary>
public sealed record SettingsFile(SettingsFileStatus Status, JsonConfigurationSource Source)
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
        var at = ConfigurationSources.IndexOfEnvironment(configuration.Sources);
        var file = new JsonConfigurationSource { Path = path, Optional = true, ReloadOnChange = false }; // nothing reloads: every change waits for a restart
        file.ResolveFileProvider();

        if (!File.Exists(path)) return new(SettingsFileStatus.Used(path, exists: false), file);
        var problems = Problems(configuration, file, at);
        if (problems.Count == 0 || SettingsCheck.Check(builder.Configuration).Count > 0)
        {
            // The file is fine, or the environment is wrong on its own (the start fails as it always did, on the environment's settings).
            configuration.Sources.Insert(at, file);
            return new(SettingsFileStatus.Used(path, exists: true), file);
        }
        return new(SettingsFileStatus.LeftOut(path, problems), file);
    }

    private static IReadOnlyList<string> Problems(IConfigurationBuilder configuration, JsonConfigurationSource file, int at)
    {
        try
        {
            return SettingsCheck.Check(ConfigurationSources.With(configuration, file, at, replacing: false));
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException)
        {
            return ["The file is not valid JSON."]; // the parser's text would quote the file
        }
    }
}
